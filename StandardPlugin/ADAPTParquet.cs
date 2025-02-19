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

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class ADAPTParquetWriter
    {
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
                    using (ParquetRowGroupWriter rg = writer.CreateRowGroup())
                    {
                        int index = 0;
                        if (ColumnData.Timestamps.Any())
                        {
                            await rg.WriteColumnAsync(new DataColumn(Schema.DataFields[index], ColumnData.Timestamps.Select(t => t.ToString("O", CultureInfo.InvariantCulture)).ToArray()));
                        }
                        foreach (var doubleColumn in ColumnData.Columns)
                        {
                            await rg.WriteColumnAsync(new DataColumn(Schema.DataFields[++index], doubleColumn.Values.ToArray()));
                        }
                        await rg.WriteColumnAsync(new DataColumn(Schema.DataFields[++index], ColumnData.Geometries.ToArray()));
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

        public void AddOperationData(OperationData operationData, Catalog catalog, Implement implement, CommonExporters commonExporters)
        {
            bool hasMultipleProducts = implement.Sections.Any(x => x.ProductIndexWorkingData != null);
            foreach (NumericWorkingData nwd in implement.GetDistinctWorkingDatas())
            {
                if (hasMultipleProducts &&
                    (commonExporters.TypeMappings.FirstOrDefault(m => m.Source == nwd.Representation.Code)?.IsMultiProductCapable ?? false))
                {
                    foreach (var productId in operationData.ProductIds)
                    {
                        string productName = catalog.Products.First(p => p.Id.ReferenceId == productId).Description;
                        if (!Columns.Any(c => c.SrcName == nwd.Representation.Code && c.ProductId == productId.ToString()))
                        {
                            Columns.Add(new ADAPTDataColumn(nwd, commonExporters, productId.ToString(), productName));
                        }
                    }
                }
                else
                {
                    if (!Columns.Any(c => c.SrcName == nwd.Representation.Code))
                    {
                        Columns.Add(new ADAPTDataColumn(nwd, commonExporters));
                    }
                }
            }
        }

        public List<DateTime> Timestamps { get; set; }

        public List<ADAPTDataColumn> Columns { get; set; }

        public List<byte[]> Geometries { get; set; }

        public int GetDataColumnIndex(ADAPTDataColumn dataColumn)
        {
            var columnIndex = Columns.IndexOf(dataColumn);
            if (columnIndex != -1)
            {
                columnIndex += 2; //Index is 1-based
            }

            return columnIndex;
        }
    }

    internal class ADAPTDataColumn
    {
        public ADAPTDataColumn(NumericWorkingData numericWorkingData, CommonExporters commonExporters, string productId = null, string productName = null)
        {
            SrcObject = numericWorkingData;
            SrcName = numericWorkingData.Representation.Code;
            SrcUOMCode = numericWorkingData.UnitOfMeasure.Code;
            TargetName = commonExporters.TypeMappings.First(m => m.Source == numericWorkingData.Representation.Code).Target;
            TargetUOMCode = commonExporters.StandardDataTypes.Definitions.First(x => x.DefinitionCode == TargetName).NumericDataTypeDefinitionAttributes.UnitOfMeasureCode;
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

        public NumericWorkingData SrcObject { get; set; }

        public List<double?> Values { get; set; }
    }
}
