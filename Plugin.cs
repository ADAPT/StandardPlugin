using AgGateway.ADAPT.ApplicationDataModel.ADM;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace AgGateway.ADAPT.StandardPlugin
{

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

        public void Export(ApplicationDataModel.ADM.ApplicationDataModel dataModel, string exportPath, Properties properties = null)
        {
            var root = new Standard.Root
            {
                Catalog = new Standard.Catalog(),
                Documents = new Standard.Documents()
            };

            var catalogErrors = CatalogExporter.Export(dataModel, root, properties);
            ((List<IError>)Errors).AddRange(catalogErrors);
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
