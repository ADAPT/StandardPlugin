using AgGateway.ADAPT.ApplicationDataModel.ADM;
using System.Collections.Generic;


namespace AgGateway.ADAPT.StandardPlugin
{

    public static class CatalogExporter
    {
        public static IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel dataModel, Standard.Root exportRoot, Properties properties = null)
        {
            List<IError> errors = new List<IError>();
            exportRoot.Catalog.Growers = ExportGrowers(dataModel.Catalog.Growers);
            //TODO the rest
            return errors;
        }

        public static Standard.Id ExportID(ApplicationDataModel.Common.CompoundIdentifier srcId)
        {
            var id = new Standard.Id { ReferenceId = srcId.ReferenceId.ToString() };
            foreach (var srcUniqueId in srcId.UniqueIds)
            {
                var uniqueId = new Standard.UniqueIdElement
                {
                    IdText = srcUniqueId.Id,
                    IdSource = string.IsNullOrEmpty(srcUniqueId.Source) ? srcUniqueId.Source : null,
                };
                switch (srcUniqueId.IdType) 
                {
                    case ApplicationDataModel.Common.IdTypeEnum.UUID: uniqueId.IdTypeCode = "UUID"; break;
                    case ApplicationDataModel.Common.IdTypeEnum.LongInt: uniqueId.IdTypeCode = "LONGINT"; break;
                    case ApplicationDataModel.Common.IdTypeEnum.String: uniqueId.IdTypeCode = "STRING"; break;
                    case ApplicationDataModel.Common.IdTypeEnum.URI: uniqueId.IdTypeCode = "URI"; break;
                }
                switch(srcUniqueId.SourceType)
                {
                    case ApplicationDataModel.Common.IdSourceTypeEnum.URI: uniqueId.IdSourceTypeCode = "URI"; break;
                    case ApplicationDataModel.Common.IdSourceTypeEnum.GLN: uniqueId.IdSourceTypeCode = "GLN"; break;
                }
                id.UniqueIds.Add(uniqueId);
            }
            return id;
        }

        public static List<Standard.GrowerElement> ExportGrowers(IEnumerable<AgGateway.ADAPT.ApplicationDataModel.Logistics.Grower> srcGrowers)
        {
            List<Standard.GrowerElement> output = new List<Standard.GrowerElement>();
            foreach (var frameworkGrower in srcGrowers)
            {
                Standard.GrowerElement grower = new Standard.GrowerElement()
                {
                    Name = frameworkGrower.Name,
                    Id = ExportID(frameworkGrower.Id),
                    //TODO Context Items
                    //TODO Contact Info -> ADAPT Standard Party
                };
                output.Add(grower);
            }
            return output;
        }

    }
}