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
            _root.Documents.WorkOrders = new List<WorkOrderElement>();
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
                if (dvcString != null && Enum.TryParse(geomString, out SourceDeviceDefinition definition))
                {
                    _deviceDefinition = definition;
                }
            }
        }

        public static IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel model, Root root, string exportPath, Properties properties)
        {
            WorkRecordExporter exporter = new WorkRecordExporter(root, exportPath, properties);
            return exporter.Export(model);
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
                Dictionary<string, List<VariableElement>> variablesByOutputKey = new Dictionary<string, List<VariableElement>>();
                Dictionary<string, LoggedData> loggedDataByOutputKey = new Dictionary<string, LoggedData>();
                foreach (var fieldLoggedData in fieldIdGroupBy)
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

                        if (!variablesByOutputKey.ContainsKey(outputOperationKey))
                        {
                            variablesByOutputKey.Add(outputOperationKey, new List<VariableElement>());
                        }
                        ExportOperationSpatialRecords(columnDataByOutputKey[outputOperationKey], implement, operationData, variablesByOutputKey[outputOperationKey]);

                        loggedDataByOutputKey[outputOperationKey] = fieldLoggedData;
                    }
                }

                int exportIndex = 0;
                Directory.CreateDirectory(_exportPath);

                foreach (var kvp in sourceOperationsByOutputKey)
                {
                    string outputFileName = (++exportIndex).ToString() + ".parquet";
                    var loggedData = loggedDataByOutputKey[kvp.Key];
                    var summary = model.Documents.Summaries.FirstOrDefault(x => x.Id.ReferenceId == loggedData.SummaryId);
                    var productIds = kvp.Value.SelectMany(x => x.ProductIds).Distinct();
                    var variables = variablesByOutputKey[kvp.Key];

                    Standard.OperationElement outputOperation = new OperationElement()
                    {
                        OperationTypeCode = _commonExporters.ExportOperationType(kvp.Value.First().OperationType),
                        ContextItems = _commonExporters.ExportContextItems(kvp.Value.First().ContextItems),
                        Name = string.Join(";", kvp.Value.Select(x => x.Description)),
                        Variables = variables,
                        ProductIds = productIds.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList(),
                        SpatialRecordsFile = outputFileName,
                        GuidanceAllocations = _commonExporters.ExportGuidanceAllocations(loggedData.GuidanceAllocationIds, model),
                        PartyRoles = _commonExporters.ExportPersonRoles(model.Catalog.PersonRoles.Where(x => loggedData.PersonRoleIds.Contains(x.Id.ReferenceId)).ToList()),
                        SummaryValues = ExportSummaryValues(summary, variables, productIds),
                    };

                    ExportLoad(kvp.Value, model.Documents.Loads, outputOperation);

                    //Output any spatial data
                    string outputFile = Path.Combine(_exportPath, outputFileName);
                    ADAPTParquetWriter writer = new ADAPTParquetWriter(columnDataByOutputKey[kvp.Key]);
                    AsyncContext.Run(async () => await writer.Write(outputFile));

                    var loggedDataIdAsString = loggedData.Id.ReferenceId.ToString(CultureInfo.InvariantCulture);
                    var workOrder = _root.Documents.WorkOrders.FirstOrDefault(x => x.Id.ReferenceId == loggedDataIdAsString);
                    if (workOrder == null)
                    {
                        workOrder = new WorkOrderElement
                        {
                            Operations = new List<OperationElement>(),
                            FieldId = loggedData.FieldId?.ToString(CultureInfo.InvariantCulture),
                            Id = _commonExporters.ExportID(loggedData.Id),
                            CropZoneId = loggedData.CropZoneId?.ToString(CultureInfo.InvariantCulture),
                            Name = loggedData.Description,
                            Notes = loggedData.Notes.Select(x => x.Description).ToList(),
                            TimeScopes = _commonExporters.ExportTimeScopes(loggedData.TimeScopes, out var seasonIds),
                            SeasonId = seasonIds?.FirstOrDefault()
                        };
                        _root.Documents.WorkOrders.Add(workOrder);
                    }
                    workOrder.Operations.Add(outputOperation);
                }
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
                var variableElement = GetOrCreateVariableEment(variables, kvp.Key);
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

        private VariableElement GetOrCreateVariableEment(List<VariableElement> variables, string variableName)
        {
            var variableElement = variables.FirstOrDefault(x => x.Name == variableName);
            if (variableElement == null)
            {
                if (!_commonExporters.TypeMappings.TryGetValue(variableName, out var definitionCode))
                {
                    return null;
                }

                variableElement = new VariableElement
                {
                    DefinitionCode = definitionCode,
                    Name = variableName,
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
                var variableElement = GetOrCreateVariableEment(operationElement.Variables, srcLoad.LoadQuantity.Representation.Code)
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

        private void ExportOperationSpatialRecords(ADAPTParquetColumnData runningOutput, Implement implement, OperationData operationData, List<VariableElement> variables)
        {
            SpatialRecord priorSpatialRecord = null;
            foreach (var record in operationData.GetSpatialRecords())
            {
                foreach (var section in implement.Sections)
                {
                    if (section.IsEngaged(record) &&
                        section.TryGetCoveragePolygon(record, priorSpatialRecord, out NetTopologySuite.Geometries.Polygon polygon))
                    {
                        runningOutput.Geometries.Add(polygon.ToBinary());

                        runningOutput.Timestamps.Add(record.Timestamp);

                        foreach (var dataColumn in runningOutput.Columns)
                        {
                            var workingData = section.NumericDefinitions.FirstOrDefault(x => x.Representation.Code == dataColumn.SrcName);
                            double? doubleVal = null;
                            if (workingData != null)
                            {
                                if (record.GetMeterValue(workingData) is NumericRepresentationValue value)
                                {
                                    doubleVal = value.AsConvertedDouble(dataColumn.TargetUOMCode);
                                }
                            }
                            dataColumn.Values.Add(doubleVal);

                            //TODO Multivariety - see left/right example https://adaptstandard.org/docs/scenario-001/
                            //If only one product on the operation, we can just set it on the relevant variables (rate, depth, etc.)
                            //If more than one, we need to create an additional variable per product during the record iteration as vrProductIndex changes
                            //The product that is set on a section gets the rate, the other products get a 0
                            if (!variables.Any(v => v.Name == dataColumn.SrcName)) //TODO improve on doing this conditional every time
                            {
                                VariableElement variable = new VariableElement()
                                {
                                    Name = dataColumn.SrcName,
                                    DefinitionCode = dataColumn.TargetName,
                                    //ProductId =  TODO multivariety etc.
                                    Id = _commonExporters.ExportID(dataColumn.SrcObject.Id),
                                    FileDataIndex = runningOutput.GetDataColumnIndex(dataColumn),
                                    //TODO rest.  
                                    //Rate properties are only relevant for Work Order variables, not here
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
        }
    }
}