using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Common;
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
            return exporter.Export(model);
        }

        private IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel model)
        {
            Directory.CreateDirectory(_exportPath);
            foreach (var fieldIdGroupBy in model.Documents.LoggedData.GroupBy(ld => ld.FieldId))
            {
                //TODO Summaries
                //If a one-to-one mapping between source and destination operations, then we can just create SummaryValues on the Operation from the OperationSummaries src data (OperationSummaries mapped to src OperationDatas)
                //If many-to-one, OperationSummaries should be summed or averaged (depending on presence of unit denominator)
                //For the Sumamaries at the LoggedData level, LoggedData generates only one OperationData, then we can copy those summary values onto the operation
                //If LoggedDatas get split in such a way that we cannot logically map them to the output, omit them.


                //Dictionary<string, LoggedData> loggedDataByOutputKey = new Dictionary<string, LoggedData>();
                foreach (var fieldLoggedData in fieldIdGroupBy)
                {
                    List<OperationDefinition> groupedOperations = new List<OperationDefinition>();
                    foreach (var operationData in fieldLoggedData.OperationData)
                    {
                        if (!operationData.GetSpatialRecords().Any())
                        {
                            continue;
                        }

                        Implement implement = new Implement(operationData, model.Catalog, _geometryPositition, _deviceDefinition, _commonExporters.TypeMappings);

                        //Output grouping
                        OperationDefinition operationDefinition = new OperationDefinition(implement, operationData);
                        var matchingOperation = groupedOperations.FirstOrDefault(x => x.IsMatchingOperation(operationDefinition));
                        if (matchingOperation != null)
                        {
                            //This is the logical equivalent of another operation
                            matchingOperation.SourceOperations.Add(new OperationImplementPair(operationData, implement));
                        }
                        else
                        {
                            //First one in this group
                            groupedOperations.Add(operationDefinition);
                            _errors.AddRange(operationDefinition.ColumnData.AddOperationData(operationData, model.Catalog, operationDefinition.Implement, _commonExporters));

                            VariableElement timestamp = new VariableElement()
                            {
                                Name = "Timestamp",
                                DefinitionCode = "Timestamp",
                                Id = new Id() { ReferenceId = string.Concat(operationDefinition.Key, "-timestamp") },
                                FileDataIndex = 1
                            };
                            operationDefinition.VariablesByOutputName.Add("Timestamp", timestamp);
                            foreach (var dataColumn in operationDefinition.ColumnData.Columns)
                            {
                                if (!operationDefinition.VariablesByOutputName.ContainsKey(dataColumn.TargetName))
                                {
                                    VariableElement variable = new VariableElement()
                                    {
                                        Name = dataColumn.SrcName,
                                        DefinitionCode = dataColumn.TargetName,
                                        ProductId = dataColumn.ProductId,
                                        Id = _commonExporters.ExportID(dataColumn.SrcObject.Id),
                                        FileDataIndex = operationDefinition.ColumnData.GetDataColumnIndex(dataColumn),
                                    };
                                    operationDefinition.VariablesByOutputName.Add(dataColumn.TargetName, variable);
                                }
                            }
                        }
                    }

                    foreach (var operationDefinition in groupedOperations)
                    {
                        foreach (var sourceOperationPair in operationDefinition.SourceOperations)
                        {
                            ExportOperationSpatialRecords(operationDefinition.ColumnData, sourceOperationPair.Implement, sourceOperationPair.OperationData);
                        }
                        ExportOperation(operationDefinition, model, fieldLoggedData);

                        //Remove any operations that have no spatial data and no summary data
                        // if ((!columnDataByOutputKey[outputOperationKey].Timestamps.Any() || !columnDataByOutputKey[outputOperationKey].Geometries.Any()) &&
                        //     (!loggedDataByOutputKey.ContainsKey(outputOperationKey) || loggedDataByOutputKey[outputOperationKey].SummaryId == null))
                        // {
                        //     columnDataByOutputKey.Remove(outputOperationKey);
                        //     loggedDataByOutputKey.Remove(outputOperationKey);
                        //     sourceOperationsByOutputKey.Remove(outputOperationKey);
                        // }
                        // else
                        // {
                        //     loggedDataByOutputKey[outputOperationKey] = fieldLoggedData;
                        // }
                    }
                    if (fieldLoggedData.ReleaseSpatialData != null)
                    {
                        fieldLoggedData.ReleaseSpatialData();
                    }
                }
            }

            if (!_root.Documents.WorkRecords.Any())
            {
                _root.Documents.WorkRecords = null;
            }

            _errors.AddRange(_commonExporters.Errors);
            return _errors;
        }

        private void ExportOperation(OperationDefinition operationDefinition, ApplicationDataModel.ADM.ApplicationDataModel model, LoggedData srcLoggedData)
        {

            //Give the parquet file a meaningful name
            string operationType = Enum.GetName(typeof(AgGateway.ADAPT.ApplicationDataModel.Common.OperationTypeEnum), operationDefinition.OperationType);
            var firstPair = operationDefinition.SourceOperations.First();
            string products = "";
            if (firstPair.OperationData.ProductIds.Any())
            {
                products = "_" + firstPair.OperationData.ProductIds
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

            var summary = model.Documents.Summaries.FirstOrDefault(x => x.Id.ReferenceId == srcLoggedData.SummaryId);
            var productIds = firstPair.OperationData.ProductIds;
            var variables = operationDefinition.VariablesByOutputName.Values.ToList();
            string joinedName = string.Join(";", operationDefinition.SourceOperations.Select(x => x.OperationData.Description));
            string name = string.IsNullOrEmpty(joinedName) ? srcLoggedData.Description + "_" + operationType : joinedName;

            Standard.OperationElement outputOperation = new OperationElement()
            {
                OperationTypeCode = _commonExporters.ExportOperationType(operationDefinition.OperationType),
                ContextItems = _commonExporters.ExportContextItems(firstPair.OperationData.ContextItems),
                Name = name,
                Variables = variables,
                ProductIds = productIds.Any() ? productIds.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList() : null,
                SpatialRecordsFile = outputFileName,
                GuidanceAllocations = _commonExporters.ExportGuidanceAllocations(srcLoggedData.GuidanceAllocationIds, model),
                PartyRoles = _commonExporters.ExportPersonRoles(model.Catalog.PersonRoles.Where(x => srcLoggedData.PersonRoleIds.Contains(x.Id.ReferenceId)).ToList()),
                SummaryValues = ExportSummaryValues(summary, variables, productIds),
            };

            ExportLoad(operationDefinition.SourceOperations.Select(x => x.OperationData), model.Documents.Loads, outputOperation);

            //Output any spatial data
            string outputFile = Path.Combine(_exportPath, outputFileName);
            int duplicateIndex = 1;
            while (File.Exists(outputFile))
            {
                var newName = string.Concat(Path.GetFileNameWithoutExtension(outputFileName), "_", duplicateIndex++.ToString(), Path.GetExtension(outputFileName));
                outputFile = Path.Combine(_exportPath, newName);
                outputOperation.SpatialRecordsFile = newName;
            }
            ADAPTParquetWriter writer = new ADAPTParquetWriter(operationDefinition.ColumnData);
            AsyncContext.Run(async () => await writer.Write(outputFile));

            var loggedDataIdAsString = srcLoggedData.Id.ReferenceId.ToString(CultureInfo.InvariantCulture);
            var workRecord = _root.Documents.WorkRecords.FirstOrDefault(x => x.Id.ReferenceId == loggedDataIdAsString);
            string fieldId = srcLoggedData.FieldId?.ToString(CultureInfo.InvariantCulture) ?? CatalogExporter.UnknownFieldId;
            if (workRecord == null)
            {
                workRecord = new WorkRecordElement
                {
                    Operations = new List<OperationElement>(),
                    FieldId = fieldId,
                    Id = _commonExporters.ExportID(srcLoggedData.Id),
                    CropZoneId = srcLoggedData.CropZoneId?.ToString(CultureInfo.InvariantCulture),
                    Name = srcLoggedData.Description,
                    Notes = srcLoggedData.Notes.Any() ? srcLoggedData.Notes.Select(x => x.Description).ToList() : null,
                    TimeScopes = _commonExporters.ExportTimeScopes(srcLoggedData.TimeScopes, out var seasonIds),
                    SeasonId = seasonIds?.FirstOrDefault()
                };
                _root.Documents.WorkRecords.Add(workRecord);
            }
            workRecord.Operations.Add(outputOperation);
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

        private void ExportLoad(IEnumerable<OperationData> srcOperations, IEnumerable<Load> srcloads, OperationElement operationElement)
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
                    if (record.Timestamp.Minute == 6 && record.Timestamp.Second == 39)
                    {
                        int x = 0;
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

    internal class OperationDefinition
    {
        internal OperationDefinition(Implement implement, OperationData operationData)
        {
            Implement = implement;
            OperationDate = operationData.GetSpatialRecords().First().Timestamp;
            SourceOperations = new List<OperationImplementPair>() { new OperationImplementPair(operationData, implement) };
            LoadId = operationData.LoadId ?? 0;
            PrescriptionId = operationData.PrescriptionId ?? 0;
            WorkItemOperationId = operationData.WorkItemOperationId ?? 0;
            ProductIds = string.Join("|", operationData.ProductIds);
            OperationType = operationData.OperationType;
            Key = operationData.Id.ReferenceId.ToString();
            VariablesByOutputName = new Dictionary<string, VariableElement>();
            ColumnData = new ADAPTParquetColumnData();
        }
        internal Implement Implement { get; set; }
        internal DateTime OperationDate { get; set; }
        internal List<OperationImplementPair> SourceOperations { get; set; }

        internal int LoadId { get; set; }
        internal int PrescriptionId { get; set; }
        internal int WorkItemOperationId { get; set; }
        internal string ProductIds { get; set; }
        internal OperationTypeEnum OperationType { get; set; }
        internal Dictionary<string, VariableElement> VariablesByOutputName { get; set; }
        internal ADAPTParquetColumnData ColumnData { get; set; }
        internal string Key { get; private set; }
        internal bool IsMatchingOperation(OperationDefinition other)
        {
            if (LoadId != other.LoadId ||
                PrescriptionId != other.PrescriptionId ||
                WorkItemOperationId != other.WorkItemOperationId ||
                OperationType != other.OperationType ||
                ProductIds != other.ProductIds ||
                other.Implement.GetImplementDefinitionKey() != Implement.GetImplementDefinitionKey())
            {
                return false;
            }
            else
            {
                return DatesAreSimilar(OperationDate, other.OperationDate);
            }
        }

        private bool DatesAreSimilar(DateTime first, DateTime second)
        {
            return Math.Abs((first - second).TotalHours) < 36d;
        }
    }

    internal class OperationImplementPair
    {
        public OperationImplementPair(OperationData operationData, Implement implement)
        {
            OperationData = operationData;
            Implement = implement;
        }
        internal OperationData OperationData { get; set; } 
        internal Implement Implement { get; set; }
    }
}