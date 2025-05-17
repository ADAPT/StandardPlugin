using Parquet.Schema;
using Parquet;
using Parquet.Data;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using AgGateway.ADAPT.ApplicationDataModel.LoggedData;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Prescriptions;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class ADAPTParquetWriter
    {
        const int RowGroupSize = 65535;
        public static Dictionary<string, string> GeoParquetMetadata
        {
            get
            {
                return new Dictionary<string, string>
                {
                    ["geo"] =
                    "{\"version\":\"1.0.0\",\"primary_column\":\"geometry\",\"columns\":{\"geometry\":{\"encoding\":\"WKB\",\"geometry_types\":[\"Polygon\"]}}}"
                };
            }
        }

        public ADAPTParquetWriter(ADAPTParquetColumnData columnData)
        {
            List<DataField> dataFields = new List<DataField>();
            if (columnData.Timestamps.Any())
            {
                dataFields.Add(new DataField<string>("timestamp"));
            }
            dataFields.AddRange(columnData.Columns.Select(n => new DataField<double?>(n.TargetName)));
            dataFields.Add(new DataField<byte[]>("geometry"));
            Schema = new ParquetSchema(dataFields);
            ColumnData = columnData;
        }

        private ParquetSchema Schema { get; set; }
        private ADAPTParquetColumnData ColumnData { get; set; }

        public async Task Write(string outputFile)
        {
            using (var fs = File.Create(outputFile))
            {
                using (ParquetWriter writer = await ParquetWriter.CreateAsync(Schema, fs))
                {
                    writer.CustomMetadata = GeoParquetMetadata;
                    int startIndex = 0;
                    int remainingRowGroups = ColumnData.Geometries.Count / RowGroupSize;
                    if (ColumnData.Geometries.Count % RowGroupSize > 0)
                    {
                        remainingRowGroups++;
                    }
                    while (remainingRowGroups > 0)
                    {
                        using (ParquetRowGroupWriter rg = writer.CreateRowGroup())
                        {
                            int index = -1;
                            if (ColumnData.Timestamps.Any())
                            {
                                var timestamps = ColumnData.Timestamps.Skip(startIndex).Skip(startIndex).Take(RowGroupSize);
                                await rg.WriteColumnAsync(new DataColumn(Schema.DataFields[++index], timestamps.Select(t => t.ToString("O", CultureInfo.InvariantCulture)).ToArray()));
                            }
                            foreach (var doubleColumn in ColumnData.Columns)
                            {
                                var values = doubleColumn.Values.Skip(startIndex).Take(RowGroupSize);
                                await rg.WriteColumnAsync(new DataColumn(Schema.DataFields[++index], values.ToArray()));
                            }
                            var geometries = ColumnData.Geometries.Skip(startIndex).Take(RowGroupSize);
                            await rg.WriteColumnAsync(new DataColumn(Schema.DataFields[++index], geometries.ToArray()));
                        }
                        startIndex += RowGroupSize;
                        remainingRowGroups--;
                    }
                }
            }
        }
    }

    internal class ADAPTParquetColumnData
    {
        public ADAPTParquetColumnData()
        {
            Timestamps = new List<DateTime>();
            Columns = new List<ADAPTDataColumn>();
            Geometries = new List<byte[]>();
        }

        public void AddVectorPrescription(VectorPrescription rx, List<WorkOrderExportColumn> exportColumns, Catalog catalog, CommonExporters commonExporters)
        {
            foreach (var exportColumn in exportColumns)
            {
                string targetName = exportColumn.Variable.DefinitionCode;
                string targetUOMCode = commonExporters.StandardDataTypes.Definitions.First(x => x.DefinitionCode == targetName).NumericDataTypeDefinitionAttributes.UnitOfMeasureCode;
                string productName = catalog.Products.First(x => x.Id.ReferenceId == Int32.Parse(exportColumn.Variable.ProductId)).Description;

                Columns.Add(new ADAPTDataColumn(exportColumn.ProductLookup, targetName, targetUOMCode, exportColumn.Variable.ProductId, productName));
            }
        }

        public List<IError> AddOperationData(OperationData operationData, Catalog catalog, Implement implement, CommonExporters commonExporters)
        {
            List<IError> errors = new List<IError>();
            bool hasMultipleProducts = implement.Sections.Any(x => x.ProductIndexWorkingData != null);
            foreach (NumericWorkingData nwd in implement.GetDistinctWorkingDatas())
            {
                string targetName = commonExporters.TypeMappings.First(m => m.Source == nwd.Representation.Code).Target;
                string targetUOMCode = commonExporters.StandardDataTypes.Definitions.First(x => x.DefinitionCode == targetName).NumericDataTypeDefinitionAttributes.UnitOfMeasureCode;
                if (!nwd.CanConvertInto(targetUOMCode))
                {
                    errors.Add(new Error
                    {
                        Description = $"Omitting {nwd.Representation.Code} in {nwd.UnitOfMeasure.Code} because it cannot be converted to {targetUOMCode}",
                    });
                    continue;
                }
                if (hasMultipleProducts &&
                    (commonExporters.TypeMappings.FirstOrDefault(m => m.Source == nwd.Representation.Code)?.IsMultiProductCapable ?? false))
                {
                    foreach (var productId in operationData.ProductIds)
                    {
                        string productName = catalog.Products.First(p => p.Id.ReferenceId == productId).Description;
                        if (!Columns.Any(c => c.SrcName == nwd.Representation.Code && c.ProductId == productId.ToString()))
                        {
                            Columns.Add(new ADAPTDataColumn(nwd, targetName, targetUOMCode, productId.ToString(), productName));
                        }
                    }
                }
                else
                {
                    if (!Columns.Any(c => c.SrcName == nwd.Representation.Code))
                    {
                        Columns.Add(new ADAPTDataColumn(nwd, targetName, targetUOMCode));
                    }
                }
            }
            return errors;
        }

        public List<DateTime> Timestamps { get; set; }

        public List<ADAPTDataColumn> Columns { get; set; }

        public List<byte[]> Geometries { get; set; }

        public int GetDataColumnIndex(ADAPTDataColumn dataColumn)
        {
            return Columns.IndexOf(dataColumn) + 1;  //Index is 1-based
        }
    }

    internal class ADAPTDataColumn
    {
        public ADAPTDataColumn(NumericWorkingData numericWorkingData, string targetName, string targetUOM, string productId = null, string productName = null)
        {
            SrcWorkingData = numericWorkingData;
            SrcName = numericWorkingData.Representation.Code;
            SrcUOMCode = numericWorkingData.UnitOfMeasure.Code;
            TargetName = targetName;
            TargetUOMCode = targetUOM;
            Values = new List<double?>();
            ProductId = productId;
            if (productName != null)
            {
                TargetName = $"{TargetName}_{productName}";
            }
        }

        public ADAPTDataColumn(RxProductLookup lookup, string targetName, string targetUOM, string productId = null, string productName = null)
        {
            SrcProductLookup = lookup;
            SrcName = lookup.Representation.Code;
            SrcUOMCode = lookup.UnitOfMeasure.Code;
            TargetName = targetName;
            TargetUOMCode = targetUOM;
            Values = new List<double?>();
            ProductId = productId;
            if (productName != null)
            {
                TargetName = $"{TargetName}_{productName}";
            }
        }

        public string SrcName { get; set; }

        public string SrcUOMCode { get; set; }

        public string ProductId { get; set; }

        public string TargetUOMCode { get; set; }

        public string TargetName { get; set; }

        public NumericWorkingData SrcWorkingData { get; set; }

        public RxProductLookup SrcProductLookup { get; set; }

        public List<double?> Values { get; set; }
    }
}
