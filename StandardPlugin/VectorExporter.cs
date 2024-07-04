using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.LoggedData;
using AgGateway.ADAPT.ApplicationDataModel.Representations;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class VectorExporter
    {
        internal static async Task ExportOperationSpatialRecords(OperationData operationData, Catalog catalog, SourceGeometryPosition geometryPositition, SourceDeviceDefinition deviceDefinition, string outputFile)
        {
            //TODO may need to update this to take more than one OperationData with same data definitions
            ImplementDefinition implement = new ImplementDefinition(operationData, catalog, geometryPositition, deviceDefinition);
            List<NumericWorkingData> distinctWorkingDatas = new List<NumericWorkingData>();
            foreach (var nwd in implement.Sections.SelectMany(s => s.NumericDefinitions))
            {
                if (!distinctWorkingDatas.Any(d => d.Representation.Code == nwd.Representation.Code))
                {
                    distinctWorkingDatas.Add(nwd);
                }
            }
            ADAPTColumnData columnData = new ADAPTColumnData(distinctWorkingDatas);

            foreach (var record in operationData.GetSpatialRecords())
            {
                foreach (var section in implement.Sections.Where(s => s.IsEngaged(record)))
                {
                    columnData.Timestamps.Add(record.Timestamp);
                    //TODO section geometry

                    foreach (var dataColumn in columnData.Columns)
                    {
                        var workingData = section.NumericDefinitions.Single(x => x.Representation.Code == dataColumn.SrcName);
                        var value = record.GetMeterValue(workingData) as NumericRepresentationValue;
                        if (value != null)
                        {
                            dataColumn.Values.Add(value.AsConvertedDouble(dataColumn.TargetUOMCode));
                        }
                        else
                        {
                            dataColumn.Values.Add(null);
                        }
                    }
                }
            }

            ADAPTParquetWriter writer = new ADAPTParquetWriter(columnData);
            await writer.Write(outputFile);
        }
    }
}