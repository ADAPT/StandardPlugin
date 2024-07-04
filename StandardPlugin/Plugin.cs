using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.Standard;

namespace AgGateway.ADAPT.StandardPlugin
{

    public enum SourceGeometryPosition
    {
        GPSReceiver = 0, 
        ImplementReferencePoint = 1,   
    }

    public enum SourceDeviceDefinition
    {
        DeviceElementHierarchy = 0, 
        Machine0Implement1Section2 = 1, 
    }

    public class Plugin : IPlugin
    {
        public Plugin()
        {
            Errors = new List<IError>();
        }

        public string Name => "ADAPT Standard Plugin";

        public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;

        public string Owner => "AgGateway";

        public IList<IError> Errors { get; set; }

        public async void Export(ApplicationDataModel.ADM.ApplicationDataModel dataModel, string exportPath, Properties properties = null)
        {
            var root = new Root
            {
                Catalog = new Standard.Catalog(),
                Documents = new Standard.Documents()
            };

            var catalogErrors = CatalogExporter.Export(dataModel, root, properties);
            var prescriptionErrors = PrescriptionExporter.Export(dataModel, root, exportPath, properties);

            int exportIndex = 0;
            foreach (var loggedData in dataModel.Documents.LoggedData)
            {
                //TODO Define Operations, correctly combining & separating
                foreach (var operationData in loggedData.OperationData)
                {
                    string outputFile = Path.Combine(exportPath + (++exportIndex).ToString() + ".parquet"); //TODO improve on this
                    //TODO get export switches from the properties
                    await VectorExporter.ExportOperationSpatialRecords(operationData, dataModel.Catalog, SourceGeometryPosition.GPSReceiver, SourceDeviceDefinition.DeviceElementHierarchy, outputFile);
                }
            }
            
            var errors = Errors as List<IError>;
            errors.AddRange(catalogErrors);
            errors.AddRange(prescriptionErrors);
        }

        public Properties GetProperties(string dataPath)
        {
            throw new NotImplementedException("This plugin only supports exporting from the ADAPT Fraemwork to the ADAPT Standard.");
        }

        public IList<ApplicationDataModel.ADM.ApplicationDataModel> Import(string dataPath, Properties properties = null)
        {
            throw new NotImplementedException("This plugin only supports exporting from the ADAPT Fraemwork to the ADAPT Standard.");
        }

        public void Initialize(string args = null)
        {
            //Nothing to initialize
        }

        public bool IsDataCardSupported(string dataPath, Properties properties = null)
        {
            throw new NotImplementedException("This plugin only supports exporting from the ADAPT Fraemwork to the ADAPT Standard.");
        }

        public IList<IError> ValidateDataOnCard(string dataPath, Properties properties = null)
        {
            throw new NotImplementedException("This plugin only supports exporting from the ADAPT Fraemwork to the ADAPT Standard.");
        }
    }
}
