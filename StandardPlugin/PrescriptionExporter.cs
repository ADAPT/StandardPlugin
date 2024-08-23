using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Prescriptions;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using AgGateway.ADAPT.Standard;
using BitMiracle.LibTiff.Classic;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class PrescriptionExporter
    {
        private readonly string _exportPath;
        private readonly Standard.Documents _documents;
        private readonly Standard.Catalog _catalog;
        private readonly List<IError> _errors;
        private readonly CommonExporters _commonExporters;
        private Tiff.TiffExtendProc _parentTagExtender;

        private PrescriptionExporter(Root root, string exportPath)
        {
            _exportPath = exportPath;
            _documents = root.Documents;
            _catalog = root.Catalog;
            _errors = new List<IError>();

            _documents.WorkOrders = new List<WorkOrderElement>();

            _commonExporters = new CommonExporters(root);
        }

        internal static IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel dataModel, Root root, string exportPath, Properties properties = null)
        {
            var exporter = new PrescriptionExporter(root, exportPath);
            return exporter.Export(dataModel);
        }

        private IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel dataModel)
        {
            ExportRasterGridPrescriptions(dataModel.Catalog.Prescriptions);

            _errors.AddRange(_commonExporters.Errors);
            return _errors;
        }

        private void ExportRasterGridPrescriptions(IEnumerable<Prescription> srcPrescriptions)
        {
            if (srcPrescriptions.IsNullOrEmpty())
            {
                return;
            }

            foreach (var frameworkPrescription in srcPrescriptions)
            {
                if (!(frameworkPrescription is RasterGridPrescription frameworkRxPrescription))
                {
                    continue;
                }

                var rxPrescription = new OperationElement
                {
                    Id = _commonExporters.ExportID(frameworkRxPrescription.Id),
                    Name = frameworkRxPrescription.Description,
                    OperationTypeCode = _commonExporters.ExportOperationType(frameworkRxPrescription.OperationType),
                    ProductIds = frameworkRxPrescription.ProductIds.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList(),
                    Variables = ExportRxProductLookups(frameworkRxPrescription.RxProductLookups),
                    TimeScopes = _commonExporters.ExportTimeScopes(frameworkRxPrescription.TimeScopes, out _),
                    PartyRoles = _commonExporters.ExportPersonRoles(frameworkRxPrescription.PersonRoles),
                    SpatialRecordsFile = ExportRates(frameworkRxPrescription),
                    ContextItems = _commonExporters.ExportContextItems(frameworkRxPrescription.ContextItems)
                };

                _documents.WorkOrders.Add(new WorkOrderElement
                {
                    Operations = new List<OperationElement> { rxPrescription }
                });
            }

        }

        private List<VariableElement> ExportRxProductLookups(List<RxProductLookup> srcRxProductLookups)
        {
            if (srcRxProductLookups.IsNullOrEmpty())
            {
                return null;
            }

            List<VariableElement> output = new List<VariableElement>();
            for (int index = 0; index < srcRxProductLookups.Count; index ++)
            {
                var frameworkRxProductLookup = srcRxProductLookups[index];
                var variable = new VariableElement
                {
                    Id = _commonExporters.ExportID(frameworkRxProductLookup.Id),
                    FileDataIndex = index + 1,
                    LossOfGnssRate = ExportNumericValue(frameworkRxProductLookup.LossOfGpsRate),
                    OutOfFieldRate = ExportNumericValue(frameworkRxProductLookup.OutOfFieldRate),
                    ProductId = frameworkRxProductLookup.ProductId?.ToString(CultureInfo.InvariantCulture),
                    // TODO: Should map to ADAPT Standard Code
                    DefinitionCode = frameworkRxProductLookup.Representation?.Code
                };
                output.Add(variable);
            }
            return output;
        }

        private double? ExportNumericValue(NumericRepresentationValue srcNumericValue)
        {
            return srcNumericValue?.Value?.Value;
        }

        private string ExportRates(RasterGridPrescription srcRxPrescription)
        {
            if (srcRxPrescription.Rates.IsNullOrEmpty())
            {
                return null;
            }

            if (!ValidateInputs(srcRxPrescription))
            {
                return null;
            }

            _parentTagExtender = Tiff.SetTagExtender(TagExtender);

            var fileName = $"{srcRxPrescription.Description}{srcRxPrescription.Id.ReferenceId}.tiff";
            string outputPath = Path.Combine(_exportPath, fileName);
            using (var tiff = Tiff.Open(outputPath, "w"))
            {
                var productCount = srcRxPrescription.Rates.First().RxRates.Select(x => x.RxProductLookupId).Distinct().Count();
                var min = srcRxPrescription.Rates.SelectMany(x => x.RxRates).Min(x => x.Rate);
                var max = srcRxPrescription.Rates.SelectMany(x => x.RxRates).Max(x => x.Rate);

                var columnCount = srcRxPrescription.ColumnCount;
                var rowCount = srcRxPrescription.RowCount;
                tiff.SetField(TiffTag.IMAGEWIDTH, columnCount);
                tiff.SetField(TiffTag.IMAGELENGTH, rowCount);
                tiff.SetField(TiffTag.SAMPLESPERPIXEL, productCount);
                tiff.SetField(TiffTag.SAMPLEFORMAT, SampleFormat.IEEEFP);
                tiff.SetField(TiffTag.BITSPERSAMPLE, 64);
                tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                tiff.SetField(TiffTag.ROWSPERSTRIP, 1);
                tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);

                tiff.SetField(TiffTag.COMPRESSION, Compression.DEFLATE);

                // Rows are ordered bottom to top in RxPrescription but in GeoTIFF they are stored top to bottom.
                // Set model tie point to north-west corner which corresponds to the last row in input data
                // and reverse the order of cells later on.

                // Model tie point specifies location of a point in raster space (first 3 numbers) in vector space (last 3 numbers)
                var originX = srcRxPrescription.Origin.X;
                var originY = srcRxPrescription.Origin.Y + srcRxPrescription.CellHeight.Value.Value;
                double[] tiePoints = new double[] { 0, rowCount - 1, 0, originX, originY, 0 };
                tiff.SetField(TiffTag.GEOTIFF_MODELTIEPOINTTAG, 6, tiePoints);

                double[] pixelScale = new double[] { srcRxPrescription.CellWidth.Value.Value, srcRxPrescription.CellHeight.Value.Value, 0 };
                tiff.SetField(TiffTag.GEOTIFF_MODELPIXELSCALETAG, 3, pixelScale);

                // First line a header, defining version and how many values follows.
                // Each next line consist of 4 fields: geo key, value location/type, how many values, actual value
                short[] geoDir = new short[4 * 4] 
                {
                    // KeyDirectoryVersion (1), KeyRevision (1), MinorRevision (2), NumberOfKeys (3 - how many additional lines)
                      1,    1,  2,  3 ,
                    // GTModelTypeGeoKey (1024), TIFFTagLocation (0 - short), Count (1), ModelTypeGeographic (2 - Geographic latitude-longitude System)
                    1024,   0,  1,  2 ,
                    // GTRasterTypeGeoKey (1025), TIFFTagLocation (0 - short), Count (1), RasterPixelIsArea (1)
                    1025,   0,  1,  1 ,
                    // GeographicTypeGeoKey (2048), TIFFTagLocation (0 - short), Count (1), GCS_WGS_84 (4326)
                    2048,   0,  1,  4326 
                };
                tiff.SetField(TiffTag.GEOTIFF_GEOKEYDIRECTORYTAG, geoDir.Length, geoDir);

                var rows = srcRxPrescription.Rates.Batch(columnCount).ToList();
                rows.Reverse();
                for ( int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var cells = rows[rowIndex];
                    var byteData = cells.SelectMany(x => x.RxRates).SelectMany(x => BitConverter.GetBytes(x.Rate)).ToArray();
                    tiff.WriteEncodedStrip(rowIndex, byteData, byteData.Length);
                }
            }

            Tiff.SetTagExtender(_parentTagExtender);
            return fileName;
        }

        private bool ValidateInputs(RasterGridPrescription srcRxPrescription)
        {
            var uom = srcRxPrescription.CellHeight.Value.UnitOfMeasure?.Code;
            if (uom != null && !uom.EqualsIgnoreCase("arcdeg"))
            {
                _errors.Add(new Error
                {
                    Id = srcRxPrescription.Id.ReferenceId.ToString(CultureInfo.InvariantCulture),
                    Description = "CellHeight unif of measure should either be null or arcdeg.",
                });
                return false;
            }

            uom = srcRxPrescription.CellWidth.Value.UnitOfMeasure?.Code;
            if (uom != null && !uom.EqualsIgnoreCase("arcdeg"))
            {
                _errors.Add(new Error
                {
                    Id = srcRxPrescription.Id.ReferenceId.ToString(CultureInfo.InvariantCulture),
                    Description = "CellWidth unif of measure should either be null or arcdeg.",
                });
                return false;
            }

            return true;
        }

        private void TagExtender(Tiff tiff)
        {
            TiffFieldInfo[] tiffFieldInfo =
            {
                new TiffFieldInfo(TiffTag.GEOTIFF_MODELTIEPOINTTAG, 6, 6, TiffType.DOUBLE, FieldBit.Custom, false, true, "MODELTILEPOINTTAG"),
                new TiffFieldInfo(TiffTag.GEOTIFF_MODELPIXELSCALETAG, 3, 3, TiffType.DOUBLE, FieldBit.Custom, false, true, "MODELPIXELSCALETAG"),
                new TiffFieldInfo(TiffTag.GEOTIFF_GEOKEYDIRECTORYTAG, 4 * 4, 4 * 4, TiffType.SHORT, FieldBit.Custom, false, true, "GEOKEYDIRECTORYTAG"),
            };

            tiff.MergeFieldInfo(tiffFieldInfo, tiffFieldInfo.Length);

            _parentTagExtender?.Invoke(tiff);
        }
    }
}