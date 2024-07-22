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
        public Implement(OperationData operation, Catalog catalog, SourceGeometryPosition position, SourceDeviceDefinition definition)
        {
            Sections = new List<SectionDefinition>();

            List<DeviceElementUse> allDeviceElementUses = new List<DeviceElementUse>();
            for (int depth = 0; depth < operation.MaxDepth; depth++)
            {
                foreach (var deviceElementUse in operation.GetDeviceElementUses(depth))
                {
                    allDeviceElementUses.Add(deviceElementUse);
                }
            }

            if (definition == SourceDeviceDefinition.DeviceElementHierarchy)
            {
                foreach (var lowestDeviceElementUse in operation.GetDeviceElementUses(operation.MaxDepth))
                {
                    var deviceElementConfig = catalog.DeviceElementConfigurations.FirstOrDefault(d => d.Id.ReferenceId == lowestDeviceElementUse.DeviceConfigurationId);
                    if (deviceElementConfig != null)
                    {
                        var deviceElement = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == deviceElementConfig.DeviceElementId);
                        SectionDefinition section = new SectionDefinition(lowestDeviceElementUse, deviceElementConfig, deviceElement);
                        while (deviceElement != null)
                        {
                            TopDeviceElement = deviceElement; //Keep overwriting this until we get the top Device Element

                            if (deviceElement != section.DeviceElement)
                            {
                                var ancestorConfigs = catalog.DeviceElementConfigurations.Where(x => x.DeviceElementId == deviceElement.Id.ReferenceId);
                                foreach (var ancestorConfig in ancestorConfigs)
                                {
                                    section.Offset.Add(ancestorConfig.AsOffset());

                                    foreach (var ancestorUse in allDeviceElementUses.Where(x => x.DeviceConfigurationId == ancestorConfig.Id.ReferenceId))
                                    {
                                        section.AddAncestorWorkingDatas(ancestorUse);
                                    }
                                }
                            }

                            //At the top level, the parent id often maps to the device model
                            DeviceModel = catalog.DeviceModels.FirstOrDefault(d => d.Id.ReferenceId == deviceElement.ParentDeviceId);
                            if (DeviceModel == null)
                            { 
                                //We are not at the top
                                deviceElement = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == deviceElement.ParentDeviceId);
                            }
                        }

                        if (position == SourceGeometryPosition.GPSReceiver)
                        {
                            //Add any tractor offset
                            var equipConfig = operation.EquipmentConfigurationIds.Select(x => catalog.EquipmentConfigurations.First(e => e.Id.ReferenceId == x)).FirstOrDefault();
                            if (equipConfig != null)
                            {
                                Connector vehicle = catalog.Connectors.FirstOrDefault(c => c.Id.ReferenceId == equipConfig.Connector1Id);
                                if (vehicle != null)
                                {
                                    MachineConfiguration machineConfiguration = catalog.DeviceElementConfigurations.OfType<MachineConfiguration>().FirstOrDefault(m => m.Id.ReferenceId == vehicle.DeviceElementConfigurationId);
                                    if (machineConfiguration != null)
                                    {
                                        section.Offset.Add(machineConfiguration.AsOffset());
                                    }
                                }
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
                            SectionDefinition section = new SectionDefinition(sectionUse, sectionConfiguration, null);
                            section.AddAncestorWorkingDatas(implementUse);
                            Sections.Add(section);
                        }
                    }
                }
                else
                {
                    Sections.Add(new SectionDefinition(implementUse, implementConfiguration, null));
                    TopDeviceElement = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == implementConfiguration.DeviceElementId);
                    //TODO is there a Device Model?
                }
            }
        }

        public DeviceElement TopDeviceElement { get; set; }
        public DeviceModel  DeviceModel { get; set; }
        public List<SectionDefinition> Sections { get; set; }

        public string GetDefinitionKey()
        {
            StringBuilder builder = new StringBuilder();
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
            foreach (var nwd in Sections.SelectMany(s => s.NumericDefinitions))
            {
                if (!distinctWorkingDatas.Any(d => d.Representation.Code == nwd.Representation.Code))
                {
                    distinctWorkingDatas.Add(nwd);
                }
            }
            return distinctWorkingDatas;
        }
    }

}