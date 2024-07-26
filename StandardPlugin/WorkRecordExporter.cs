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
         private readonly CommonExporters _commonExporters;
        private Standard.Root _root;

        private readonly string _exportPath;
        private WorkRecordExporter(Root root, string exportPath, Properties properties)
        {
            _exportPath = exportPath;
            _root = root;
            _commonExporters = new CommonExporters(root);
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

            foreach (int fieldId in model.Documents.LoggedData.Select(ld => ld.FieldId).Distinct())
            {
                Dictionary<string, ADAPTParquetColumnData> columnDataByOutputKey = new Dictionary<string, ADAPTParquetColumnData>();
                Dictionary<string, List<OperationData>> sourceOperationsByOutputKey = new Dictionary<string, List<OperationData>>();
                foreach (var fieldLoggedData in model.Documents.LoggedData.Where(ld => ld.FieldId == fieldId))
                {
                    foreach (var operationData in fieldLoggedData.OperationData)
                    {
                        Implement implement = new Implement(operationData, model.Catalog, _geometryPositition, _deviceDefinition, _commonExporters.TypeMappings);
                        string outputOperationKey = implement.GetOperationDefinitionKey(operationData);

                        //TODO write an algorithm so that if the OperationData has the same implement operation key
                        //on this same field within 3 days of another OperationData, they are grouped into the 
                        //same output operation.  For now, we'll group all field data with the same implement/op type into a single operation
                        //E.g. case would be multiple spray operations over the course of a summer with the same sprayer.
                        
                        //Output grouping
                        if (!sourceOperationsByOutputKey.ContainsKey(outputOperationKey))
                        {
                            sourceOperationsByOutputKey.Add(outputOperationKey, new List<OperationData>());
                        }
                        sourceOperationsByOutputKey[outputOperationKey].Add(operationData);

                        //Spatial data in same 
                        if (!columnDataByOutputKey.ContainsKey(outputOperationKey))
                        {
                            columnDataByOutputKey.Add(outputOperationKey, new ADAPTParquetColumnData(implement.GetDistinctWorkingDatas(), _commonExporters));

                        }
                       
                        var variables = ExportOperationSpatialRecords(columnDataByOutputKey[outputOperationKey], implement, operationData);
                        //TODO handle variables
                    }
                }

                int exportIndex = 0;
                Directory.CreateDirectory(_exportPath);
                foreach (var outputOperationKey in sourceOperationsByOutputKey.Keys)
                {
                    Standard.OperationElement outputOperation = new OperationElement()
                    {
                        OperationTypeCode = _commonExporters.ExportOperationType(sourceOperationsByOutputKey[outputOperationKey].First().OperationType),
                        //TODO all the rest of the properties
                    };

                    //Output any spatial data
                    if (columnDataByOutputKey.ContainsKey(outputOperationKey))
                    {
                        string outputFile = Path.Combine(_exportPath, (++exportIndex).ToString() + ".parquet");
                        ADAPTParquetWriter writer = new ADAPTParquetWriter(columnDataByOutputKey[outputOperationKey]);
                        await writer.Write(outputFile);
                    }
                }
            }

            //TODO add diagnostic errors
            return new List<IError>();
        }

        private List<Standard.VariableElement> ExportOperationSpatialRecords(ADAPTParquetColumnData runningOutput, Implement implement, OperationData operationData)
        {
            List<Standard.VariableElement> variables = new List<Standard.VariableElement>();
            SpatialRecord priorSpatialRecord = null;
            foreach (var record in operationData.GetSpatialRecords())
            {
                foreach (var section in implement.Sections)
                {
                   if (section.IsEngaged(record) && 
                        section.TryGetCoveragePolygon(record, priorSpatialRecord, out NetTopologySuite.Geometries.Polygon polygon) )
                    {
                        runningOutput.Geometries.Add(polygon.ToBinary());

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

                            if (!variables.Any(v => v.Name == dataColumn.SrcName)) //TODO improve on doing this conditional every time
                            {
                                VariableElement variable = new VariableElement()
                                {
                                    Name = dataColumn.SrcName,
                                    DefinitionCode = dataColumn.TargetName,
                                    //ProductId =  TODO multivariety etc.
                                    Id = _commonExporters.ExportID(dataColumn.SrcObject.Id),
                                    //TODO rest
                                };
                                variables.Add(variable);
                            }
                        }
                    }
                    else
                    {
                        section.ClearLeadingEdge();
                    }
                }
                priorSpatialRecord = record;
            }
            return variables;
        }
    }
}