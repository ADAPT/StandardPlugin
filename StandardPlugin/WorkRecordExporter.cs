using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Common;
using AgGateway.ADAPT.ApplicationDataModel.Documents;
using AgGateway.ADAPT.ApplicationDataModel.LoggedData;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using AgGateway.ADAPT.Standard;
using BitMiracle.LibTiff.Classic;
using Microsoft.Data.Analysis;
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
                List<OperationDefinition> groupedOperations = new List<OperationDefinition>();
                foreach (var fieldLoggedData in fieldIdGroupBy)
                {
                    //Group the operations with spatial data
                    foreach (var operationData in fieldLoggedData.OperationData)
                    {
                        if (operationData.GetSpatialRecords().Any())
                        {
                            Implement implement = new Implement(operationData, model.Catalog, _geometryPositition, _deviceDefinition, _commonExporters.TypeMappings);

                            //Output grouping
                            OperationDefinition operationDefinition = new OperationDefinition(implement, operationData, fieldLoggedData);
                            var matchingOperation = groupedOperations.FirstOrDefault(x => x.IsMatchingOperation(operationDefinition));
                            if (matchingOperation != null)
                            {
                                //This is the logical equivalent of another operation
                                matchingOperation.SourceOperations.Add(new ConstituentSpatialOperation(operationData, implement));
                                matchingOperation.SourceLoggedDatas.Add(fieldLoggedData);
                            }
                            else
                            {
                                //First one in this group
                                groupedOperations.Add(operationDefinition);

                                //Add the columns for parquet
                                _errors.AddRange(operationDefinition.ColumnData.AddOperationData(operationData, model.Catalog, implement, _commonExporters));

                                //Add the variables for the output operation
                                VariableElement timestamp = new VariableElement()
                                {
                                    Name = "Timestamp",
                                    DefinitionCode = "Timestamp",
                                    Id = new Id() { ReferenceId = string.Concat(operationDefinition.Key(), "-timestamp") },
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
                    }
                }

                //Add the summary data
                HashSet<Summary> sourceSummaries = new HashSet<Summary>();
                foreach (var loggedData in fieldIdGroupBy)
                {
                    var summary = model.Documents.Summaries.FirstOrDefault(x => x.LoggedDataIds.Contains(loggedData.Id.ReferenceId) || x.Id.ReferenceId == loggedData.SummaryId);
                    if (summary != null)
                    {
                        sourceSummaries.Add(summary);
                    }
                }
                foreach (var sourceSummary in sourceSummaries)
                {
                    if (sourceSummary.OperationSummaries.Any())
                    {
                        foreach (var srcOpSummary in sourceSummary.OperationSummaries)
                        {
                            var opSummaryMatched = false;
                            foreach (var groupedOperation in groupedOperations)
                            {
                                if (groupedOperation.IsMatchingOperationSummary(srcOpSummary))
                                {
                                    if (!groupedOperation.SourceSummaryValuesByProductId.ContainsKey(srcOpSummary.ProductId))
                                    {
                                        groupedOperation.SourceSummaryValuesByProductId.Add(srcOpSummary.ProductId, new List<StampedMeteredValues>());
                                    }
                                    groupedOperation.SourceSummaryValuesByProductId[srcOpSummary.ProductId].AddRange(srcOpSummary.Data);
                                    opSummaryMatched = true;
                                    break; //Operation Summary should only match to a single output
                                }
                            }
                            if (!opSummaryMatched)
                            {
                                //TODO consider summary only operations
                            }
                        }
                    }
                    else if (groupedOperations.Count == 1 &&
                          groupedOperations.First().IsMatchingSummary(sourceSummary))
                    {
                        //There is a single operation so we can match the summary
                        groupedOperations.First().SourceSummaryValuesWithoutProduct.AddRange(sourceSummary.SummaryData);
                    }
                    else if (!groupedOperations.Any())
                    {
                        //TODO consider summary only operations
                    }
                    else
                    {
                        //TODO log inability to match summary
                    }
                }


                if (groupedOperations.Any())
                {
                    var workRecordId = fieldIdGroupBy.Count() == 1 ? _commonExporters.ExportID(fieldIdGroupBy.First().Id) : new Id() { ReferenceId = string.Concat("field_", fieldIdGroupBy.First().FieldId?.ToString(), "_WorkRecord") };
                    var cropZoneId = fieldIdGroupBy.Count() == 1 ? fieldIdGroupBy.First().CropZoneId?.ToString(CultureInfo.InvariantCulture) : null;
                    var notes = fieldIdGroupBy.SelectMany(x => x.Notes).Any() ? fieldIdGroupBy.SelectMany(x => x.Notes).Select(y => y.Description).ToList() : null;
                    List<TimeScopeElement> timeScopeElements = new List<TimeScopeElement>();
                    string seasonId = null;
                    foreach (var loggedData in fieldIdGroupBy.ToList())
                    {
                        timeScopeElements.AddRange(_commonExporters.ExportTimeScopes(loggedData.TimeScopes, out var seasonIds));
                        if (seasonIds.Any())
                        {
                            seasonId = seasonIds.First();
                        }
                    }

                    var fieldWorkRecord = new WorkRecordElement
                    {
                        Operations = new List<OperationElement>(),
                        FieldId = groupedOperations.First().FieldId,
                        Id = workRecordId,
                        CropZoneId = cropZoneId,
                        Name = string.Join(";", fieldIdGroupBy.ToList().Select(x => x.Description)),
                        Notes = notes,
                        TimeScopes = timeScopeElements,
                        SeasonId = seasonId
                    };
                    _root.Documents.WorkRecords.Add(fieldWorkRecord);

                    //Having grouped the OperationData objects, export them
                    foreach (var operationDefinition in groupedOperations)
                    {
                        foreach (var constituentSpatialOperation in operationDefinition.SourceOperations)
                        {
                            //Export the spatial records into the parquet
                            ExportOperationSpatialRecords(operationDefinition.ColumnData, constituentSpatialOperation.Implement, constituentSpatialOperation.OperationData);
                        }
                        //Export the header & write out the parquet for this operation
                        if (operationDefinition.ColumnData.Geometries.Count > 0 || operationDefinition.SourceSummaryValuesByProductId.Any() || operationDefinition.SourceSummaryValuesWithoutProduct.Any())
                        {
                            ExportOperation(operationDefinition, model, fieldWorkRecord);
                        }
                    }
                }

                foreach (var loggedData in fieldIdGroupBy)
                {
                    if (loggedData.ReleaseSpatialData != null)
                    {
                        loggedData.ReleaseSpatialData();
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

        private void ExportOperation(OperationDefinition operationDefinition, ApplicationDataModel.ADM.ApplicationDataModel model, WorkRecordElement workRecord)
        {
            //Give the parquet file a meaningful name
            string operationType = Enum.GetName(typeof(AgGateway.ADAPT.ApplicationDataModel.Common.OperationTypeEnum), operationDefinition.OperationType);
            string products = "";
            if (operationDefinition.ProductIds.Any())
            {
                products = "_" + operationDefinition.ProductIds
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

            var variables = operationDefinition.VariablesByOutputName.Values.ToList();
            string joinedName = string.Join(";", operationDefinition.SourceOperations.Select(x => x.OperationData.Description));
            string name = string.IsNullOrEmpty(joinedName) ? workRecord.Name + "_" + operationType : joinedName;
            List<ContextItemElement> contextItems = new List<ContextItemElement>();
            foreach (var srcOp in operationDefinition.SourceOperations)
            {
                var items = _commonExporters.ExportContextItems(srcOp.OperationData.ContextItems);
                if (items != null)
                {
                    contextItems.AddRange(items);
                }
            }
            List<Roo> partyRoles = new List<Roo>();  //No idea why this generated as "Roo"
            List<GuidanceAllocationElement> guidanceAllocations = new List<GuidanceAllocationElement>();
            foreach (var loggedData in operationDefinition.SourceLoggedDatas)
            {
                var guidance = _commonExporters.ExportGuidanceAllocations(loggedData.GuidanceAllocationIds, model);
                if (guidance != null)
                {
                    guidanceAllocations.AddRange(guidance);
                }
                var exportedRoles = _commonExporters.ExportPersonRoles(model.Catalog.PersonRoles.Where(x => loggedData.PersonRoleIds.Contains(x.Id.ReferenceId)).ToList());
                if (exportedRoles != null)
                {
                    partyRoles.AddRange(exportedRoles);
                }
            }

            Standard.OperationElement outputOperation = new OperationElement()
            {
                OperationTypeCode = _commonExporters.ExportOperationType(operationDefinition.OperationType),
                ContextItems = contextItems.Any() ? contextItems : null,
                Name = name,
                Variables = variables,
                ProductIds = operationDefinition.ProductIds.Any() ? operationDefinition.ProductIds.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList() : null,
                SpatialRecordsFile = outputFileName,
                GuidanceAllocations = guidanceAllocations.Any() ? guidanceAllocations : null,
                PartyRoles =  partyRoles,
                SummaryValues = ExportSummaryValues(operationDefinition, variables)
            };



            ExportLoad(operationDefinition.SourceOperations.Select(x => x.OperationData), model.Documents.Loads, outputOperation);

            //Output any spatial data
            if (operationDefinition.ColumnData.Geometries.Any())
            {
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
            }
            workRecord.Operations.Add(outputOperation);
        }

        private void AppendSummaryValue(StampedMeteredValues srcValues, List<VariableElement> variables, List<SummaryValueElement> output, int? productId = null)
        {
            foreach (var summaryItem in srcValues.Values.Select(x => x.Value).OfType<NumericRepresentationValue>())
            {
                var variable = GetOrCreateVariableElement(variables, summaryItem.Representation.Code, productId);
                if (variable != null)
                {
                    var targetUoM = _commonExporters.StandardDataTypes.Definitions.First(x => x.DefinitionCode == variable.DefinitionCode).NumericDataTypeDefinitionAttributes.UnitOfMeasureCode;
                    var value = summaryItem.AsConvertedDouble(targetUoM);
                    if (value.HasValue)
                    {
                        SummaryValueElement summaryValueElement = new SummaryValueElement()
                        {
                            TimeScopes = _commonExporters.ExportTimeScopes(new List<TimeScope>() { srcValues.Stamp }, out _),
                            ValueText = value.Value.ToString(),
                            VariableId = variable.Id.ReferenceId
                        };
                        output.Add(summaryValueElement);
                    }
                    else
                    {
                        //TODO log failed summary value or product not represented in spatial data
                    }
                }
                else
                {
                    //TODO log failed summary value
                }
            }
        }

        private List<SummaryValueElement> ExportSummaryValues(OperationDefinition operationDefinition, List<VariableElement> variables)
        {
            List<SummaryValueElement> output = new List<SummaryValueElement>();
            foreach (var summaryKvp in operationDefinition.SourceSummaryValuesByProductId)
            {
                foreach (var srcValue in summaryKvp.Value)
                {
                    AppendSummaryValue(srcValue, variables, output, summaryKvp.Key);
                }
            }
            foreach (var generalSummary in operationDefinition.SourceSummaryValuesWithoutProduct)
            {
                AppendSummaryValue(generalSummary, variables, output);
            }

            return output;
        }

        private VariableElement GetOrCreateVariableElement(List<VariableElement> variables, string srcVariableName, int? productId = null)
        {
            var variableElement = variables.FirstOrDefault(x => x.Name == srcVariableName && x.ProductId == productId?.ToString());
            if (variableElement == null)
            {
                if (!_commonExporters.TypeMappings.Any(m => m.Source == srcVariableName))
                {
                    //TODO custom data type definition?
                    return null;
                }

                variableElement = new VariableElement
                {
                    DefinitionCode = _commonExporters.TypeMappings.First(m => m.Source == srcVariableName).Target,
                    Name = srcVariableName,
                    Id = new Id { ReferenceId = string.Format(CultureInfo.InvariantCulture, "total-{0}", ++_variableCounter) },
                    ProductId = productId?.ToString()
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
                    //TODO remove
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
                                    dataColumn.Values.Add(0d); //We've grouped operations together and this doesn't apply.
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
        internal OperationDefinition(Implement implement, OperationData operationData, LoggedData loggedData)
        {
            ImplementKey = implement.GetImplementDefinitionKey();
            SourceOperations = new List<ConstituentSpatialOperation>() { new ConstituentSpatialOperation(operationData, implement) };
            LoadId = operationData.LoadId ?? 0;
            PrescriptionId = operationData.PrescriptionId ?? 0;
            WorkItemOperationId = operationData.WorkItemOperationId ?? 0;
            ProductIds = operationData.ProductIds.ToList();
            OperationType = operationData.OperationType;
            VariablesByOutputName = new Dictionary<string, VariableElement>();
            ColumnData = new ADAPTParquetColumnData();
            SourceLoggedDatas = new HashSet<LoggedData>() { loggedData };
            FieldId = loggedData.FieldId?.ToString(CultureInfo.InvariantCulture) ?? CatalogExporter.UnknownFieldId;
            SourceSummaryValuesByProductId = new Dictionary<int, List<StampedMeteredValues>>();
            SourceSummaryValuesWithoutProduct = new List<StampedMeteredValues>();
        }

        internal string ImplementKey { get; private set; }
        internal List<ConstituentSpatialOperation> SourceOperations { get; set; }
        internal Dictionary<int, List<StampedMeteredValues>> SourceSummaryValuesByProductId { get; set; }
        internal List<StampedMeteredValues> SourceSummaryValuesWithoutProduct { get; set; }
        internal HashSet<LoggedData> SourceLoggedDatas { get; set; }
        internal int LoadId { get; set; }
        internal string FieldId { get; set; }
        internal int PrescriptionId { get; set; }
        internal int WorkItemOperationId { get; set; }
        internal List<int> ProductIds { get; set; }
        internal OperationTypeEnum OperationType { get; set; }
        internal Dictionary<string, VariableElement> VariablesByOutputName { get; set; }
        internal ADAPTParquetColumnData ColumnData { get; set; }
        internal string Key()
        {
            return string.Join(";", SourceOperations.Select(x => x.OperationData.Id.ReferenceId.ToString()));
        }
        internal string ProductKey()
        {
            return string.Join(";", ProductIds.OrderBy(x => x).ToString());
        }

        internal bool IsMatchingOperation(OperationDefinition other)
        {
            if (LoadId != other.LoadId ||
                PrescriptionId != other.PrescriptionId ||
                WorkItemOperationId != other.WorkItemOperationId ||
                OperationType != other.OperationType ||
                ProductKey() != other.ProductKey() ||
                other.ImplementKey != ImplementKey)
            {
                return false;
            }
            else
            {
                return SourceOperations.Any(x => DatesAreSimilar(x.Date, other.SourceOperations.First().Date)); //We expect "other" to have only a single source operation
            }
        }

        internal bool IsMatchingSummary(Summary summary)
        {
            return SourceLoggedDatas.Select(x => x.SummaryId == summary.Id.ReferenceId).Any() ||
                    SourceLoggedDatas.Select(x => x.Id.ReferenceId).Any(y => summary.LoggedDataIds.Contains(y));
        }

        internal bool IsMatchingOperationSummary(OperationSummary operationSummary)
        {
            return operationSummary.OperationType == OperationType &&
                   operationSummary.ProductId != 0 &&
                   ProductIds.Contains(operationSummary.ProductId);
        }

        private bool DatesAreSimilar(DateTime first, DateTime second)
        {
            return Math.Abs((first - second).TotalHours) < 36d;
        }
    }

    internal class ConstituentSpatialOperation 
    {
        public ConstituentSpatialOperation(OperationData operationData, Implement implement)
        {
            Implement = implement;
            OperationData = operationData;
            Date = operationData.GetSpatialRecords().First().Timestamp;
        }
        internal Implement Implement { get; set; }
        internal OperationData OperationData { get; set; }
        internal DateTime Date { get; set; }
    }
}