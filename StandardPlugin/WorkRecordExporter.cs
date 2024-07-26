using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.LoggedData;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using AgGateway.ADAPT.Standard;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class WorkRecordExporter
    {
        private List<IError> _errors;
        private readonly SourceGeometryPosition _geometryPositition;
        private readonly SourceDeviceDefinition _deviceDefinition;
        private Standard.Catalog _catalog;

        private readonly string _exportPath;
        private WorkRecordExporter(Root root, string exportPath, Properties properties)
        {
            _exportPath = exportPath;
            _catalog = root.Catalog;
            _geometryPositition = SourceGeometryPosition.GPSReceiver;
            _deviceDefinition = SourceDeviceDefinition.DeviceElementHierarchy;
            if (properties != null)
            {
                var geomString = properties.GetProperty("SourceGeometryPosition");
                var dvcString = properties.GetProperty("SourceDeviceDefinition");
                if (geomString != null && Enum.TryParse(geomString, out SourceGeometryPosition position))
                {
                    _geometryPositition = position;
                }
                if (dvcString != null && Enum.TryParse(geomString, out SourceDeviceDefinition definition))
                {
                    _deviceDefinition = definition;
                }
            }
        }

        public static async Task<IEnumerable<IError>> Export(ApplicationDataModel.ADM.ApplicationDataModel model, Root root, string exportPath, Properties properties)
        {
            WorkRecordExporter exporter = new WorkRecordExporter(root, exportPath, properties);
            return await exporter.Export(model);
        }
        
        private async Task<IEnumerable<IError>> Export(ApplicationDataModel.ADM.ApplicationDataModel model)
        {
            //TODO this logic will need to be rewritten so that
            //1. We  create a separate work record for each Field and each set of OperationDatas of the same Operation Type within a few days of one another, regardless of what Logged Data they are in
            //2. We need to group OperationData objects into Operations inside the WorkRecord based on identical OperationTypes, implement keys, and starting on the same day
            //3. Each Operation should have 1 parquet export file.
            
            Dictionary<string, ADAPTColumnData> columnDataByImplement = new Dictionary<string, ADAPTColumnData>();
            foreach (var loggedData in model.Documents.LoggedData)
            {
                foreach (var operationData in loggedData.OperationData)
                {
                   Implement implement = new Implement(operationData, model.Catalog, _geometryPositition, _deviceDefinition);
                    string implementKey = implement.GetDefinitionKey();

                    if (!columnDataByImplement.ContainsKey(implementKey))
                    {
                        columnDataByImplement.Add(implementKey, new ADAPTColumnData(implement.GetDistinctWorkingDatas()));  
                    }
                    ExportOperationSpatialRecords(columnDataByImplement[implementKey], implement, operationData);
                }
            }

            int exportIndex = 0;
            Directory.CreateDirectory(_exportPath);
            foreach(var implementKey in columnDataByImplement.Keys)
            {
                //TODO possiblity for too much data in memory
                string outputFile = Path.Combine(_exportPath, (++exportIndex).ToString() + ".parquet"); 
                ADAPTParquetWriter writer = new ADAPTParquetWriter(columnDataByImplement[implementKey]);
                await writer.Write(outputFile);
            }

            //TODO add diagnostic errors
            return new List<IError>();
        }

        private void ExportOperationSpatialRecords(ADAPTColumnData runningOutput, Implement implement, OperationData operationData)
        {
            SpatialRecord priorSpatialRecord = null;
            foreach (var record in operationData.GetSpatialRecords())
            {
                foreach (var section in implement.Sections)
                {
                    runningOutput.Timestamps.Add(record.Timestamp);

                    foreach (var dataColumn in runningOutput.Columns)
                    {
                        var workingData = section.NumericDefinitions.FirstOrDefault(x => x.Representation.Code == dataColumn.SrcName);
                        double? doubleVal = null;
                        if (workingData != null)
                        {
                            var value = record.GetMeterValue(workingData) as NumericRepresentationValue;
                            if (value != null)
                            {
                                doubleVal = value.AsConvertedDouble(dataColumn.TargetUOMCode);
                            }
                        }
                        dataColumn.Values.Add(doubleVal);

                    }
                    if (section.IsEngaged(record) && 
                        section.TryGetCoveragePolygon(record, priorSpatialRecord, out NetTopologySuite.Geometries.Polygon polygon) )
                    {
                        runningOutput.Geometries.Add(polygon.ToBinary());
                    }
                    else
                    {
                        section.ClearLeadingEdge();
                    }
                }
                priorSpatialRecord = record;
            }
        }
    }
}