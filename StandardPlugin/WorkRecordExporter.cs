using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Documents;
using AgGateway.ADAPT.ApplicationDataModel.LoggedData;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using AgGateway.ADAPT.Standard;
using Nito.AsyncEx;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class WorkRecordExporter
    {
        private readonly List<IError> _errors;
        private readonly SourceGeometryPosition _geometryPositition;
        private readonly SourceDeviceDefinition _deviceDefinition;
        private readonly CommonExporters _commonExporters;
        private readonly Standard.Root _root;
        private int _variableCounter;

        private readonly string _exportPath;
        private WorkRecordExporter(Root root, string exportPath, Properties properties)
        {
            _variableCounter = 0;
            _exportPath = exportPath;
            _root = root;
            _root.Documents.WorkRecords = new List<WorkRecordElement>();
            _errors = new List<IError>();
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
                if (dvcString != null && Enum.TryParse(dvcString, out SourceDeviceDefinition definition))
                {
                    _deviceDefinition = definition;
                }
            }
        }

        public static IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel model, Root root, string exportPath, Properties properties)
        {
            WorkRecordExporter exporter = new WorkRecordExporter(root, exportPath, properties);
            if (model.Documents?.LoggedData != null)
            {
                return exporter.Export(model);
            }
            else
            {
                return new List<IError>();
            }
        }

        private IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel model)
        {
            foreach (var fieldIdGroupBy in model.Documents.LoggedData.GroupBy(ld => ld.FieldId))
            {
                //TODO Summaries
                //If a one-to-one mapping between source and destination operations, then we can just create SummaryValues on the Operation from the OperationSummaries src data (OperationSummaries mapped to src OperationDatas)
                //If many-to-one, OperationSummaries should be summed or averaged (depending on presence of unit denominator)
                //For the Sumamaries at the LoggedData level, LoggedData generates only one OperationData, then we can copy those summary values onto the operation
                //If LoggedDatas get split in such a way that we cannot logically map them to the output, omit them.

                Dictionary<string, ADAPTParquetColumnData> columnDataByOutputKey = new Dictionary<string, ADAPTParquetColumnData>();
                Dictionary<string, List<OperationData>> sourceOperationsByOutputKey = new Dictionary<string, List<OperationData>>();
                Dictionary<string, Dictionary<string, VariableElement>> variablesByTargetNameByOutputKey = new Dictionary<string, Dictionary<string, VariableElement>>();
                Dictionary<string, LoggedData> loggedDataByOutputKey = new Dictionary<string, LoggedData>();
                foreach (var fieldLoggedData in fieldIdGroupBy)
                {
                    foreach (var operationData in fieldLoggedData.OperationData)
                    {
                        if (!operationData.GetSpatialRecords().Any())
                        {
                            continue;
                        }

                        Implement implement = new Implement(operationData, model.Catalog, _geometryPositition, _deviceDefinition, _commonExporters.TypeMappings);
                        string outputOperationKey = operationData.Id.ReferenceId.ToString(); //TODO //implement.GetOperationDefinitionKey(operationData);  //Commented out pending completion of the below grouping logic to avoid any potential issues.  OperationData taken as is for now.

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
                            columnDataByOutputKey.Add(outputOperationKey, new ADAPTParquetColumnData());
                        }
                        _errors.AddRange(columnDataByOutputKey[outputOperationKey].AddOperationData(operationData, model.Catalog, implement, _commonExporters));

                        //Variables
                        if (!variablesByTargetNameByOutputKey.ContainsKey(outputOperationKey))
                        {
                            VariableElement timestamp = new VariableElement()
                            {
                                Name = "Timestamp",
                                DefinitionCode = "Timestamp",
                                Id = new Id() { ReferenceId = string.Concat(outputOperationKey, "-timestamp") },
                                FileDataIndex = 1
                            };
                            variablesByTargetNameByOutputKey.Add(outputOperationKey, new Dictionary<string, VariableElement>());
                            variablesByTargetNameByOutputKey[outputOperationKey].Add("Timestamp", timestamp);
                        }
                        foreach (var dataColumn in columnDataByOutputKey[outputOperationKey].Columns)
                        {
                            if (!variablesByTargetNameByOutputKey[outputOperationKey].ContainsKey(dataColumn.TargetName))
                            {
                                VariableElement variable = new VariableElement()
                                {
                                    Name = dataColumn.SrcName,
                                    DefinitionCode = dataColumn.TargetName,
                                    ProductId = dataColumn.ProductId,
                                    Id = _commonExporters.ExportID(dataColumn.SrcWorkingData.Id),
                                    FileDataIndex = columnDataByOutputKey[outputOperationKey].GetDataColumnIndex(dataColumn),
                                };
                                variablesByTargetNameByOutputKey[outputOperationKey].Add(dataColumn.TargetName, variable);
                            }
                        }

                        ExportOperationSpatialRecords(columnDataByOutputKey[outputOperationKey], implement, operationData);

                        //Remove any operations that have no spatial data and no summary data
                        if ((!columnDataByOutputKey[outputOperationKey].Timestamps.Any() || !columnDataByOutputKey[outputOperationKey].Geometries.Any()) &&
                            (!loggedDataByOutputKey.ContainsKey(outputOperationKey) || loggedDataByOutputKey[outputOperationKey].SummaryId == null))
                        {
                            columnDataByOutputKey.Remove(outputOperationKey);
                            loggedDataByOutputKey.Remove(outputOperationKey);
                            sourceOperationsByOutputKey.Remove(outputOperationKey);
                        }
                        else
                        {
                            loggedDataByOutputKey[outputOperationKey] = fieldLoggedData;
                        }
                    }
                    if (fieldLoggedData.ReleaseSpatialData != null)
                    {
                        fieldLoggedData.ReleaseSpatialData();
                    }
                }
                Directory.CreateDirectory(_exportPath);


                foreach (var kvp in sourceOperationsByOutputKey)
                {
                    //Give the parquet file a meaningful name
                    string operationType = Enum.GetName(typeof(AgGateway.ADAPT.ApplicationDataModel.Common.OperationTypeEnum), kvp.Value.First().OperationType);
                    string products = "";
                    if (kvp.Value.SelectMany(od => od.ProductIds).Any())
                    {
                        products = "_" + kvp.Value.SelectMany(x =>
                                    x.ProductIds)
                                    .Distinct()
                                    .Select(r =>
                                        model.Catalog.Products.First(p =>
                                            p.Id.ReferenceId == r).Description)
                                    .Aggregate((i, j) => i + "_" + j);
                    }
                    string outputFileName = string.Concat(operationType, products, ".parquet");
                    outputFileName = new string(outputFileName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                    if (outputFileName.Length > 255)
                    {
                        outputFileName = outputFileName.Substring(0, 255);
                    }

                    var loggedData = loggedDataByOutputKey[kvp.Key];
                    var summary = model.Documents.Summaries.FirstOrDefault(x => x.Id.ReferenceId == loggedData.SummaryId);
                    var productIds = kvp.Value.SelectMany(x => x.ProductIds).Distinct();
                    var variables = variablesByTargetNameByOutputKey[kvp.Key].Values.ToList();
                    string name = kvp.Value.Any() ? string.Join(";", kvp.Value.Select(x => x.Description)) : loggedData.Description + "_" + operationType;

                    Standard.OperationElement outputOperation = new OperationElement()
                    {
                        OperationTypeCode = _commonExporters.ExportOperationType(kvp.Value.First().OperationType),
                        ContextItems = _commonExporters.ExportContextItems(kvp.Value.First().ContextItems),
                        Name = name,
                        Variables = variables,
                        ProductIds = productIds.Any() ? productIds.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList() : null,
                        SpatialRecordsFile = outputFileName,
                        GuidanceAllocations = _commonExporters.ExportGuidanceAllocations(loggedData.GuidanceAllocationIds, model),
                        PartyRoles = _commonExporters.ExportPersonRoles(model.Catalog.PersonRoles.Where(x => loggedData.PersonRoleIds.Contains(x.Id.ReferenceId)).ToList()),
                        SummaryValues = ExportSummaryValues(summary, variables, productIds),
                    };

                    ExportLoad(kvp.Value, model.Documents.Loads, outputOperation);

                    //Output any spatial data
                    string outputFile = Path.Combine(_exportPath, outputFileName);
                    int duplicateIndex = 1;
                    while (File.Exists(outputFile))
                    {
                        var newName = string.Concat(Path.GetFileNameWithoutExtension(outputFileName), "_", duplicateIndex++.ToString(), Path.GetExtension(outputFileName));
                        outputFile = Path.Combine(_exportPath, newName);
                        outputOperation.SpatialRecordsFile = newName;
                    }
                    ADAPTParquetWriter writer = new ADAPTParquetWriter(columnDataByOutputKey[kvp.Key]);
                    AsyncContext.Run(async () => await writer.Write(outputFile));

                    var loggedDataIdAsString = loggedData.Id.ReferenceId.ToString(CultureInfo.InvariantCulture);
                    var workRecord = _root.Documents.WorkRecords.FirstOrDefault(x => x.Id.ReferenceId == loggedDataIdAsString);
                    string fieldId = loggedData.FieldId?.ToString(CultureInfo.InvariantCulture) ?? CatalogExporter.UnknownFieldId;
                    if (workRecord == null)
                    {
                        workRecord = new WorkRecordElement
                        {
                            Operations = new List<OperationElement>(),
                            FieldId = fieldId,
                            Id = _commonExporters.ExportID(loggedData.Id),
                            CropZoneId = loggedData.CropZoneId?.ToString(CultureInfo.InvariantCulture),
                            Name = loggedData.Description,
                            Notes = loggedData.Notes.Any() ? loggedData.Notes.Select(x => x.Description).ToList() : null,
                            TimeScopes = _commonExporters.ExportTimeScopes(loggedData.TimeScopes, out var seasonIds),
                            SeasonId = seasonIds?.FirstOrDefault()
                        };
                        _root.Documents.WorkRecords.Add(workRecord);
                    }
                    workRecord.Operations.Add(outputOperation);
                }
            }

            if (!_root.Documents.WorkRecords.Any())
            {
                _root.Documents.WorkRecords = null;
            }
            
            _errors.AddRange(_commonExporters.Errors);
            return _errors;
        }

        private List<SummaryValueElement> ExportSummaryValues(Summary srcSummary, List<VariableElement> variables, IEnumerable<int> productIds)
        {
            if (srcSummary == null || 
                (srcSummary.OperationSummaries.IsNullOrEmpty() && srcSummary.SummaryData.IsNullOrEmpty()))
            {
                return null;
            }

            List<SummaryValueElement> output = new List<SummaryValueElement>();
            var operationSummaries = srcSummary.OperationSummaries?.Where(x => productIds.Contains(x.ProductId));
            var stampedMeteredValues = operationSummaries.IsNullOrEmpty()
                ? srcSummary.SummaryData
                : operationSummaries.SelectMany(x => x.Data);

            var groupedByCode = stampedMeteredValues
                .SelectMany(x => x.Values.Select(y => new { MeteredValue = y.Value as NumericRepresentationValue, StampedVaue = x }))
                .Where(x => x.MeteredValue != null)
                .GroupBy(x => x.MeteredValue.Representation.Code)
                .ToList();
            foreach (var kvp in groupedByCode)
            {
                var timeScopes = _commonExporters.ExportTimeScopes(kvp.Select(x => x.StampedVaue.Stamp).Where(x => x != null).Distinct().ToList(), out _);
                var variableElement = GetOrCreateVariableElement(variables, kvp.Key);
                if (variableElement == null)
                {
                    continue;
                }

                var targetUoM = _commonExporters.StandardDataTypes.Definitions.First(x => x.DefinitionCode == variableElement.DefinitionCode).NumericDataTypeDefinitionAttributes.UnitOfMeasureCode;
                var summaryValues = kvp.Select(x => x.MeteredValue.AsConvertedDouble(targetUoM)).Where(x => x.HasValue);
                var summaryValue = HasDenominator(targetUoM) ? summaryValues.Average() : summaryValues.Sum();
                var summary = new SummaryValueElement
                {
                    TimeScopes = timeScopes,
                    VariableId = variableElement.Id.ReferenceId,
                    ValueText = summaryValue?.ToString(CultureInfo.InvariantCulture)
                };

                output.Add(summary);
            }

            return output;
        }

        private bool HasDenominator(string unitCode)
        {
            var unitOfMeasure = Representation.UnitSystem.InternalUnitSystemManager.Instance.UnitOfMeasures[unitCode];
            return (unitOfMeasure is Representation.UnitSystem.CompositeUnitOfMeasure compUoM) && compUoM.Components.Any(x => x.Power < 0);
        }

        private VariableElement GetOrCreateVariableElement(List<VariableElement> variables, string srcVariableName)
        {
            var variableElement = variables.FirstOrDefault(x => x.Name == srcVariableName);
            if (variableElement == null)
            {
                if (!_commonExporters.TypeMappings.Any(m => m.Source == srcVariableName))
                {
                    return null;
                }

                variableElement = new VariableElement
                {
                    DefinitionCode = _commonExporters.TypeMappings.First(m => m.Source == srcVariableName).Target,
                    Name = srcVariableName,
                    Id = new Id { ReferenceId = string.Format(CultureInfo.InvariantCulture, "total-{0}", ++_variableCounter) }
                };
                variables.Add(variableElement);
            }
            return variableElement;
        }

        private void ExportLoad(List<OperationData> srcOperations, IEnumerable<Load> srcloads, OperationElement operationElement)
        {
            var loadId = srcOperations.Select(x => x.LoadId).FirstOrDefault(x => x.HasValue);
            var srcLoad = loadId.HasValue
                ? srcloads.FirstOrDefault(x => x.Id.ReferenceId == loadId)
                : null;
            if (srcLoad?.LoadQuantity != null)
            {
                var variableElement = GetOrCreateVariableElement(operationElement.Variables, srcLoad.LoadQuantity.Representation.Code)
                    ?? operationElement.Variables.FirstOrDefault(x => x.Name.EqualsIgnoreCase(srcLoad.LoadNumber));
                if (variableElement == null)
                {
                    variableElement = new VariableElement
                    {
                        Name = srcLoad.LoadNumber,
                        Id = new Id { ReferenceId = string.Format(CultureInfo.InvariantCulture, "total-{0}", ++_variableCounter) }
                    };
                    operationElement.Variables.Add(variableElement);
                }

                var loadQuantity = string.IsNullOrEmpty(variableElement.DefinitionCode)
                    ? srcLoad.LoadQuantity.Value.Value
                    : srcLoad.LoadQuantity.AsConvertedDouble(_commonExporters.StandardDataTypes.Definitions
                                                                    .First(x => x.DefinitionCode == variableElement.DefinitionCode)
                                                                    .NumericDataTypeDefinitionAttributes.UnitOfMeasureCode);

                var summary = new SummaryValueElement
                {
                    VariableId = variableElement.Id.ReferenceId,
                    ValueText = loadQuantity?.ToString(CultureInfo.InvariantCulture)
                };
                operationElement.SummaryValues.Add(summary);
            }

            operationElement.HarvestLoadIdentifier = srcLoad?.LoadNumber;
        }

        private void ExportOperationSpatialRecords(ADAPTParquetColumnData runningOutput, Implement implement, OperationData operationData)
        {
            SpatialRecord priorSpatialRecord = null;
            foreach (var record in operationData.GetSpatialRecords())
            {
                foreach (var section in implement.Sections)
                {
                    double timeDelta = 0d;
                    if (priorSpatialRecord != null)
                    {
                        timeDelta = (record.Timestamp - priorSpatialRecord.Timestamp).TotalSeconds;
                    }
                    if (section.IsEngaged(record) &&
                            timeDelta < 5d &&
                            section.TryGetCoveragePolygon(record, priorSpatialRecord, out NetTopologySuite.Geometries.Polygon polygon))
                    {
                        runningOutput.Geometries.Add(polygon.ToBinary());

                        runningOutput.Timestamps.Add(record.Timestamp);

                        foreach (var dataColumn in runningOutput.Columns)
                        {
                            if (dataColumn.ProductId != null && section.ProductIndexWorkingData != null)
                            {
                                if (section.FactoredDefinitionsBySourceCodeByProduct.ContainsKey(dataColumn.ProductId))
                                {
                                    var factoredDefinition = section.FactoredDefinitionsBySourceCodeByProduct[dataColumn.ProductId][dataColumn.SrcName];
                                    NumericRepresentationValue value = record.GetMeterValue(factoredDefinition.WorkingData) as NumericRepresentationValue;
                                    var doubleVal = value.AsConvertedDouble(dataColumn.TargetUOMCode) * factoredDefinition.Factor;

                                    NumericRepresentationValue productValue = record.GetMeterValue(section.ProductIndexWorkingData) as NumericRepresentationValue;
                                    var productValueData = productValue?.Value?.Value;
                                    if (productValueData != null && dataColumn.ProductId == ((int)productValueData).ToString())
                                    {
                                        dataColumn.Values.Add(doubleVal);
                                    }
                                    else
                                    {
                                        dataColumn.Values.Add(0d); //We're not applying this product to this section
                                    }
                                }
                                else
                                {
                                    dataColumn.Values.Add(0d); 
                                }
                            }
                            else
                            {
                                var factoredDefinition = section.FactoredDefinitionsBySourceCodeByProduct[string.Empty][dataColumn.SrcName];
                                NumericRepresentationValue value = record.GetMeterValue(factoredDefinition.WorkingData) as NumericRepresentationValue;
                                var doubleVal = value.AsConvertedDouble(dataColumn.TargetUOMCode) * factoredDefinition.Factor;
                                dataColumn.Values.Add(doubleVal);
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
        }
    }
}