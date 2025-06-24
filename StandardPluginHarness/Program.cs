using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.StandardPlugin;

var inputPath = args[0];
var outputPath = args[1];

AgGateway.ADAPT.ISOv4Plugin.Plugin sourcePlugin = new AgGateway.ADAPT.ISOv4Plugin.Plugin();
var models = sourcePlugin.Import(inputPath);

Properties properties= new Properties();
properties.SetProperty("SourceGeometryPosition", SourceGeometryPosition.GNSSReceiver.ToString());
properties.SetProperty("SourceDeviceDefinition", SourceDeviceDefinition.DeviceElementHierarchy.ToString());

AgGateway.ADAPT.StandardPlugin.Plugin outputPlugin = new AgGateway.ADAPT.StandardPlugin.Plugin();
foreach (var model in models)   
{
    outputPlugin.Export(model, outputPath, properties);
}
