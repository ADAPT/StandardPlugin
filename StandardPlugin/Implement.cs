using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Equipment;
using AgGateway.ADAPT.ApplicationDataModel.LoggedData;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class Implement
    {
        public Implement(OperationData operation, Catalog catalog, SourceGeometryPosition position, SourceDeviceDefinition definition, List<TypeMapping> typeMappings)
        {
            Sections = new List<SectionDefinition>();

            List<DeviceElementUse> allDeviceElementUses = new List<DeviceElementUse>();
            for (int depth = 0; depth < operation.MaxDepth; depth++)
            {
                allDeviceElementUses.AddRange(operation.GetDeviceElementUses(depth));
            }

            if (definition == SourceDeviceDefinition.DeviceElementHierarchy)
            {
                foreach (var lowestDeviceElementUse in operation.GetDeviceElementUses(operation.MaxDepth))
                {
                    var sectionDeviceElementConfig = catalog.DeviceElementConfigurations.FirstOrDefault(d => d.Id.ReferenceId == lowestDeviceElementUse.DeviceConfigurationId);
                    if (sectionDeviceElementConfig != null)
                    {
                        var sectionDeviceElement = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == sectionDeviceElementConfig.DeviceElementId);
                        SectionDefinition section = new SectionDefinition(lowestDeviceElementUse, sectionDeviceElementConfig, sectionDeviceElement, typeMappings);
                        DeviceElement parent = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == sectionDeviceElement.ParentDeviceId);
                        while (parent != null)
                        {
                            TopDeviceElement = parent; //Keep overwriting this until we get the top Device Element
                            var ancestorConfig = catalog.DeviceElementConfigurations.SingleOrDefault(x => x.DeviceElementId == parent.Id.ReferenceId);
                            if (ancestorConfig != null)
                            {
                                if (ancestorConfig is ImplementConfiguration)
                                {
                                    //Intermediate sections are all relative to the implement and not one another.
                                    section.Offset.Add(ancestorConfig.AsOffset());
                                }

                                foreach (var ancestorUse in allDeviceElementUses.Where(x => x.DeviceConfigurationId == ancestorConfig.Id.ReferenceId))
                                {
                                    section.AddAncestorWorkingDatas(ancestorUse, ancestorConfig, typeMappings);
                                }
                            }

                            parent = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == parent.ParentDeviceId);

                            //At the top level, the parent id often maps to the device model
                            if (sectionDeviceElement == null)
                            {
                                DeviceModel = catalog.DeviceModels.FirstOrDefault(d => d.Id.ReferenceId == TopDeviceElement.DeviceModelId);
                            }
                        }

                        if (position == SourceGeometryPosition.GPSReceiver)
                        {
                            //Add any tractor offset
                            MachineConfiguration machineConfiguration = null;
                            HitchPoint hitchPoint = null;
                            var equipConfig = operation.EquipmentConfigurationIds.Select(x => catalog.EquipmentConfigurations.FirstOrDefault(e => e.Id.ReferenceId == x)).Where(x => x != null).FirstOrDefault();
                            if (equipConfig != null)
                            {
                                Connector vehicle = catalog.Connectors.FirstOrDefault(c => c.Id.ReferenceId == equipConfig.Connector1Id);
                                if (vehicle != null)
                                {
                                    machineConfiguration = catalog.DeviceElementConfigurations.OfType<MachineConfiguration>().FirstOrDefault(m => m.Id.ReferenceId == vehicle.DeviceElementConfigurationId);
                                }
                                Connector hitch = catalog.Connectors.FirstOrDefault(c => c.Id.ReferenceId == equipConfig.Connector1Id);
                                if (hitch != null)
                                {
                                    hitchPoint = catalog.HitchPoints.FirstOrDefault(m => m.Id.ReferenceId == hitch.HitchPointId);
                                }
                            }
                            else
                            {
                                machineConfiguration = operation.EquipmentConfigurationIds.Select(x => catalog.DeviceElementConfigurations.FirstOrDefault(e => e.Id.ReferenceId == x)).Where(x => x != null).FirstOrDefault() as MachineConfiguration;
                            }

                            if (machineConfiguration != null)
                            {
                                //Add the GPS receiver offset
                                section.Offset.Add(machineConfiguration.AsOffset());
                            }
                            if (hitchPoint != null)
                            {
                                //Add the hitch point offset
                                section.Offset.Add(hitchPoint.ReferencePoint.AsOffset());
                            }
                        }
                        Sections.Add(section);
                    }
                }
            }
            else if (definition == SourceDeviceDefinition.Machine0Implement1Section2)
            {
                var vehicleUses = operation.GetDeviceElementUses(0); 
                var implementUse = operation.GetDeviceElementUses(1).FirstOrDefault();
                ImplementConfiguration implementConfiguration = catalog.DeviceElementConfigurations.FirstOrDefault(c => c.Id.ReferenceId == implementUse.DeviceConfigurationId) as ImplementConfiguration;
                if (operation.MaxDepth == 2)
                {
                    foreach (var sectionUse in operation.GetDeviceElementUses(2))
                    {
                        SectionConfiguration sectionConfiguration = catalog.DeviceElementConfigurations.First(d => d.Id.ReferenceId == sectionUse.DeviceConfigurationId) as SectionConfiguration;
                        if (sectionConfiguration != null)
                        {
                            if (sectionConfiguration.SectionWidth?.Value == null || sectionConfiguration.SectionWidth.Value.Value == 0)
                            {
                                //TODO log error
                            }
                            SectionDefinition section = new SectionDefinition(sectionUse, sectionConfiguration, null, typeMappings);
                            section.AddAncestorWorkingDatas(implementUse, implementConfiguration, typeMappings);
                            Sections.Add(section);
                        }
                    }
                }
                else
                {
                    Sections.Add(new SectionDefinition(implementUse, implementConfiguration, null, typeMappings));
                }
                TopDeviceElement = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == implementConfiguration?.DeviceElementId);
                DeviceModel = catalog.DeviceModels.FirstOrDefault(d => d.Id.ReferenceId == TopDeviceElement?.DeviceModelId);
            }

            //Apply a default width for sections that incorrectly did not report any width
            foreach (var sectionWithoutWidth in Sections.Where(s => s.WidthM == 0))
            {
                sectionWithoutWidth.WidthM = 5d / Sections.Count;
            }
        }

        public DeviceElement TopDeviceElement { get; set; }
        public DeviceModel  DeviceModel { get; set; }
        public List<SectionDefinition> Sections { get; set; }

        public string GetOperationDefinitionKey(OperationData srcOperation)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(srcOperation.OperationType.ToString());
            builder.Append("_");
            builder.Append(string.Join("|", srcOperation.ProductIds));
            builder.Append("_");
            builder.Append(srcOperation.LoadId?.ToString() ?? string.Empty);
            builder.Append(srcOperation.WorkItemOperationId?.ToString() ?? string.Empty); //TODO add these links to output operation
            builder.Append(srcOperation.PrescriptionId?.ToString() ?? string.Empty);
            builder.Append("_");
            builder.Append(DeviceModel?.Description ?? string.Empty);
            builder.Append("_");
            builder.Append(TopDeviceElement?.Description ?? string.Empty);
            builder.Append("_");
            builder.Append(TopDeviceElement?.SerialNumber ?? string.Empty);
            foreach(var section in Sections)
            {
                builder.Append(section.GetDefinitionKey());
            }
            return builder.ToString().AsMD5Hash();
        }

        public List<NumericWorkingData>  GetDistinctWorkingDatas()
        {
            List<NumericWorkingData> distinctWorkingDatas = new List<NumericWorkingData>();
            foreach (var factoredDefinition in Sections.SelectMany(s => s.FactoredDefinitionsBySourceCode.Values))
            {
                if (!distinctWorkingDatas.Any(d => d.Representation.Code == factoredDefinition.WorkingData.Representation.Code))
                {
                    distinctWorkingDatas.Add(factoredDefinition.WorkingData);
                }
            }
            return distinctWorkingDatas;
        }
    }

}