using System;
using System.Collections.Generic;
using System.Linq;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Equipment;
using AgGateway.ADAPT.ApplicationDataModel.LoggedData;
using AgGateway.ADAPT.ApplicationDataModel.Representations;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class ImplementDefinition
    {
        public ImplementDefinition(OperationData operation, Catalog catalog, SourceGeometryPosition position, SourceDeviceDefinition definition)
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
                            deviceElement = catalog.DeviceElements.FirstOrDefault(d => d.Id.ReferenceId == deviceElement.ParentDeviceId);
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
                }
            }
        }
        public List<SectionDefinition> Sections { get; set; }
    }

    internal class SectionDefinition
    {
        public SectionDefinition(DeviceElementUse deviceElementUse, DeviceElementConfiguration deviceElementConfiguration, DeviceElement deviceElement)
        {
            //DeviceElementUse = deviceElementUse;
            //DeviceElementConfiguration = deviceElementConfiguration;
            DeviceElement = deviceElement;
            
            WorkstateDefinition = deviceElementUse.GetWorkingDatas().OfType<EnumeratedWorkingData>().FirstOrDefault(x => x.Representation.Code == "dtRecordingStatus");
            NumericDefinitions = new List<NumericWorkingData>();
            NumericDefinitions.AddRange(deviceElementUse.GetWorkingDatas().OfType<NumericWorkingData>());

            if (deviceElementConfiguration is SectionConfiguration sectionConfiguration)
            {
                WidthM = sectionConfiguration.SectionWidth?.AsConvertedDouble("m") ?? 0d;
            }
            else if (deviceElementConfiguration is ImplementConfiguration implementConfiguration)
            {
                WidthM = implementConfiguration.PhysicalWidth.AsConvertedDouble("m") ?? implementConfiguration.Width?.AsConvertedDouble("m") ?? 0d;
            }

            Offset = deviceElementConfiguration.AsOffset();
        }
        public Offset Offset { get; set; }
        //public DeviceElementUse DeviceElementUse { get; set; }
        //public DeviceElementConfiguration DeviceElementConfiguration { get; set; }
        public DeviceElement DeviceElement { get; set; }
        public EnumeratedWorkingData WorkstateDefinition { get; set; }
        public List<NumericWorkingData> NumericDefinitions { get; set; }

        public double WidthM { get; set; }

        public void AddAncestorWorkingDatas(DeviceElementUse ancestorUse)
        {
            foreach (var workingData in ancestorUse.GetWorkingDatas())
            {
                if (WorkstateDefinition == null &&
                    workingData.Representation.Code == "dtRecordingStatus" &&
                    workingData is EnumeratedWorkingData ewd)
                {
                    WorkstateDefinition = ewd;
                }
                else if (workingData is NumericWorkingData nwd &&
                    !NumericDefinitions.Any(x => x.Representation.Code == nwd.Representation.Code))
                {
                    NumericDefinitions.Add(nwd);
                }
            }
        }

        public bool IsEngaged(SpatialRecord record)
        {
            if (WorkstateDefinition != null)
            {
                EnumeratedValue engagedValue = record.GetMeterValue(WorkstateDefinition) as EnumeratedValue;
                return engagedValue.Value.Value == "dtiRecordingStatusOn" || engagedValue.Value.Value == "On";
            }
            return true;
        }
    }

    public class Offset
    {
        public Offset(double? x, double? y, double? z)
        {
            X = x;
            Y = y;
            Z = z;
        }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }

        public Offset Add(Offset other)
        {
            return new Offset(((X ?? 0d) + other.X) ?? 0d,
                            ((Y ?? 0) + other.Y) ?? 0d,
                            ((Z ?? 0) + other.Z) ?? 0d);
        }
    }
}