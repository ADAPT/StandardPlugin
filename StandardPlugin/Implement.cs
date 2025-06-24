using System;
using System.Collections.Generic;
using System.ComponentModel;
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
                List<int> allDeviceElementConfigurationsReportingData = null;
                foreach (var lowestDeviceElementUse in operation.GetDeviceElementUses(operation.MaxDepth))
                {
                    var sectionDeviceElementConfig = catalog.DeviceElementConfigurations.FirstOrDefault(d => d.Id.ReferenceId == lowestDeviceElementUse.DeviceConfigurationId);
                    if (sectionDeviceElementConfig != null)
                    {
                        var sectionDeviceElement = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == sectionDeviceElementConfig.DeviceElementId);
                        if (sectionDeviceElement != null)
                        {
                            SectionDefinition section = new SectionDefinition(lowestDeviceElementUse, sectionDeviceElementConfig, sectionDeviceElement, operation, catalog, typeMappings);
                            DeviceElement parent = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == sectionDeviceElement.ParentDeviceId);
                            while (parent != null)
                            {
                                TopDeviceElement = parent; //Keep overwriting this until we get the top Device Element
                                var ancestorConfigs = catalog.DeviceElementConfigurations.Where(x => x.DeviceElementId == parent.Id.ReferenceId);
                                var ancestorCount = ancestorConfigs.Count();
                                DeviceElementConfiguration ancestorConfig = ancestorConfigs.FirstOrDefault();
                                if (ancestorCount > 1)
                                {
                                    //Some plugins have chosen to reuse the same device element for multiple device element configurations
                                    //Determine which ancestor configuration to use
                                    if (allDeviceElementConfigurationsReportingData == null)
                                    {
                                        allDeviceElementConfigurationsReportingData = GetAllDeviceElementConfiguirationsReportingData(operation);
                                    }
                                    ancestorConfig = ancestorConfigs.SingleOrDefault(x => allDeviceElementConfigurationsReportingData.Contains(x.Id.ReferenceId));
                                }

                                if (ancestorConfig != null)
                                {
                                    foreach (var ancestorUse in allDeviceElementUses.Where(x => x.DeviceConfigurationId == ancestorConfig.Id.ReferenceId))
                                    {
                                        section.AddAncestorWorkingDatas(ancestorUse, ancestorConfig, operation, typeMappings);
                                    }
                                }

                                parent = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == parent.ParentDeviceId);

                                //At the top level, the parent id often maps to the device model
                                if (sectionDeviceElement == null)
                                {
                                    DeviceModel = catalog.DeviceModels.FirstOrDefault(d => d.Id.ReferenceId == TopDeviceElement.DeviceModelId);
                                }
                            }

                            if (position == SourceGeometryPosition.GNSSReceiver)
                            {
                                //Add any tractor offset
                                MachineConfiguration machineConfiguration = null;
                                HitchPoint vehicleHitch = null;
                                ImplementConfiguration implementConfiguration = null;
                                HitchPoint implementHitch = null;
                                var equipConfig = operation.EquipmentConfigurationIds.Select(x => catalog.EquipmentConfigurations.FirstOrDefault(e => e.Id.ReferenceId == x)).Where(x => x != null).FirstOrDefault();
                                if (equipConfig != null)
                                {
                                    Connector vehicle = catalog.Connectors.FirstOrDefault(c => c.Id.ReferenceId == equipConfig.Connector1Id);
                                    if (vehicle != null)
                                    {
                                        machineConfiguration = catalog.DeviceElementConfigurations.OfType<MachineConfiguration>().FirstOrDefault(m => m.Id.ReferenceId == vehicle.DeviceElementConfigurationId);
                                        vehicleHitch = catalog.HitchPoints.FirstOrDefault(m => m.Id.ReferenceId == vehicle.HitchPointId);
                                    }

                                    Connector implement = catalog.Connectors.FirstOrDefault(c => c.Id.ReferenceId == equipConfig.Connector2Id);
                                    if (implement != null)
                                    {
                                        implementConfiguration = catalog.DeviceElementConfigurations.OfType<ImplementConfiguration>().FirstOrDefault(m => m.Id.ReferenceId == implement.DeviceElementConfigurationId);
                                        implementHitch = catalog.HitchPoints.FirstOrDefault(m => m.Id.ReferenceId == implement.HitchPointId);
                                    }
                                }
                                else
                                {
                                    machineConfiguration = operation.EquipmentConfigurationIds.Select(x => catalog.DeviceElementConfigurations.OfType<MachineConfiguration>().FirstOrDefault(e => e.Id.ReferenceId == x)).Where(x => x != null).FirstOrDefault();
                                    implementConfiguration = operation.EquipmentConfigurationIds.Select(x => catalog.DeviceElementConfigurations.OfType<ImplementConfiguration>().FirstOrDefault(e => e.Id.ReferenceId == x)).Where(x => x != null).FirstOrDefault();
                                }

                                if (machineConfiguration != null)
                                {
                                    //Add the GPS receiver offset
                                    section.Offset.Add(machineConfiguration.AsOffset());
                                }
                                if (vehicleHitch != null)
                                {
                                    //Add the hitch point offset
                                    section.Offset.Add(vehicleHitch.ReferencePoint.AsOffset());
                                }
                                if (implementConfiguration != null)
                                {
                                    //Add the implement offset
                                    section.Offset.Add(implementConfiguration.AsOffset());
                                }
                                if (implementHitch != null)
                                {
                                    //Add the implement hitch offset in the opposite inline direction
                                    section.Offset.Add(implementHitch.ReferencePoint.AsOffsetInlineReversed());
                                }
                            }
                            Sections.Add(section);
                        }
                    }
                }
            }
            else if (definition == SourceDeviceDefinition.Machine0Implement1Section2)
            {
                var vehicleUse = operation.GetDeviceElementUses(0).FirstOrDefault();
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
                            SectionDefinition section = new SectionDefinition(sectionUse, sectionConfiguration, null, operation, catalog, typeMappings);
                            section.AddAncestorWorkingDatas(implementUse, implementConfiguration, operation, typeMappings);
                            section.AddAncestorWorkingDatas(vehicleUse, implementConfiguration, operation, typeMappings);
                            Sections.Add(section);
                        }
                    }
                }
                else
                {
                    Sections.Add(new SectionDefinition(implementUse, implementConfiguration, null, operation, catalog, typeMappings));
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

        private List<int> GetAllDeviceElementConfiguirationsReportingData(OperationData operationData)
        {
            List<int> allDeviceElementConfigurationsReportingData = new List<int>();
            for (int depth = 0; depth < operationData.MaxDepth; depth++)
            {
                allDeviceElementConfigurationsReportingData.AddRange(operationData.GetDeviceElementUses(depth).Select(x => x.DeviceConfigurationId));
            }
            return allDeviceElementConfigurationsReportingData;
        }

        public DeviceElement TopDeviceElement { get; set; }
        public DeviceModel DeviceModel { get; set; }
        public List<SectionDefinition> Sections { get; set; }

        public string GetImplementDefinitionKey()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(DeviceModel?.Description ?? string.Empty);
            builder.Append("_");
            builder.Append(TopDeviceElement?.Description ?? string.Empty);
            builder.Append("_");
            builder.Append(TopDeviceElement?.SerialNumber ?? string.Empty);
            foreach (var section in Sections)
            {
                builder.Append(section.GetDefinitionKey());
            }
            return builder.ToString().AsMD5Hash();
        }

        public List<NumericWorkingData> GetDistinctWorkingDatas()
        {
            List<NumericWorkingData> distinctWorkingDatas = new List<NumericWorkingData>();
            foreach (string productId in Sections.SelectMany(s => s.FactoredDefinitionsBySourceCodeByProduct.Keys).Distinct())
            {
                foreach (var factoredDefinition in Sections.SelectMany(s => s.FactoredDefinitionsBySourceCodeByProduct[productId].Values))
                {
                    if (!distinctWorkingDatas.Any(d => d.Representation.Code == factoredDefinition.WorkingData.Representation.Code))
                    {
                        distinctWorkingDatas.Add(factoredDefinition.WorkingData);
                    }
                }
            }

            return distinctWorkingDatas;
        }
    }
}