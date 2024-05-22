using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Common;
using AgGateway.ADAPT.ApplicationDataModel.Logistics;
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

            _documents.Recommendations = new List<RecommendationElement>();

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
                    OperationTypeCode = ExportOperationType(frameworkRxPrescription.OperationType),
                    ProductIds = frameworkRxPrescription.ProductIds.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList(),
                    TimeScopes = _commonExporters.ExportTimeScopes(frameworkRxPrescription.TimeScopes),
                    PartyRoles = ExportPersonRoles(frameworkRxPrescription.PersonRoles),
                    SpatialRecordsFile = ExportRates(frameworkRxPrescription),
                    ContextItems = _commonExporters.ExportContextItems(frameworkRxPrescription.ContextItems)
                };

                _documents.Recommendations.Add(new RecommendationElement
                {
                    Operations = new List<OperationElement> { rxPrescription }
                });
            }

        }

        private string ExportRates(RasterGridPrescription srcRxPrescription)
        {
            if (srcRxPrescription.Rates.IsNullOrEmpty())
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
                tiff.SetField(TiffTag.BITSPERSAMPLE, 32);
                tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                tiff.SetField(TiffTag.ROWSPERSTRIP, 1);
                tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.PALETTE);

                //tiff.SetField(TiffTag.COMPRESSION, Compression.DEFLATE);

                double[] tiePoints = new double[] { 0, 0, 0, srcRxPrescription.Origin.X, srcRxPrescription.Origin.Y, 0 };
                tiff.SetField(TiffTag.GEOTIFF_MODELTIEPOINTTAG, 6, tiePoints);
                double[] pixelScale = new double[] { srcRxPrescription.CellHeight.Value.Value, srcRxPrescription.CellWidth.Value.Value, 0 };
                tiff.SetField(TiffTag.GEOTIFF_MODELPIXELSCALETAG, 3, pixelScale);
                short[] geoDir = new short[4 * 4] 
                {
                       1,    1,  2,  3 ,
                     1024,   0,  1,  2 ,
                     1025,   0,  1,  1 ,
                     2048,   0,  1,  4326 
                };
                tiff.SetField(TiffTag.GEOTIFF_GEOKEYDIRECTORYTAG, geoDir.Length, geoDir);

                int rowIndex = 0;
                foreach (var cells in srcRxPrescription.Rates.Batch(columnCount))
                {
                    var byteData = cells.SelectMany(x => x.RxRates).SelectMany(x => BitConverter.GetBytes(x.Rate)).ToArray();
                    tiff.WriteEncodedStrip(rowIndex, byteData, byteData.Length);
                    rowIndex++;
                }
            }

            Tiff.SetTagExtender(_parentTagExtender);
            return fileName;
        }

        private List<Roo> ExportPersonRoles(List<PersonRole> srcPersonRoles)
        {
            if (srcPersonRoles.IsNullOrEmpty())
            {
                return null;
            }

            List<Roo> output = new List<Roo>();
            foreach (var frmeworkPersonRole in srcPersonRoles)
            {
                var partyRole = new Roo
                {
                    PartyId = frmeworkPersonRole.PersonId.ToString(CultureInfo.InvariantCulture),
                    RoleCode = ExportRole(frmeworkPersonRole.Role),
                    TimeScopes = _commonExporters.ExportTimeScopes(frmeworkPersonRole.TimeScopes)
                };
                output.Add(partyRole);
            }
            return output;
        }

        private string ExportRole(EnumeratedValue role)
        {
            if (role.Representation.Code != "dtPersonRole")
            {
                _errors.Add(new Error
                {
                    Description = $"Found unsupported Role - {role.Representation.Code}",
                });
                return "UNKNOWN";
            }

            switch (role.Value.Value)
            {
                case "dtiPersonRoleAuthorizer":
                    return "AUTHORIZER";
                case "dtiPersonRoleCropAdvisor":
                    return "CROP_ADVISOR";
                case "dtiPersonRoleCustomer":
                    return "CUSTOMER";
                case "dtiPersonRoleCustomServiceProvider":
                    return "CUSTOM_SERVICE_PROVIDER";
                case "dtiPersonRoleDataServicesProvider":
                    return "DATA_SERVICES_PROVIDER";
                case "dtiPersonRoleEndUser":
                    return "END_USER";
                case "dtiPersonRoleFarmManager":
                    return "FARM_MANAGER";
                case "dtiPersonRoleFinancier":
                    return "FINANCIER";
                default:
                    return "UNKNOWN";
            }
        }

        private string ExportOperationType(OperationTypeEnum operationType)
        {
            switch (operationType)
            {
                case OperationTypeEnum.Baling:
                    return "HARVEST_PRE_HARVEST";
                case OperationTypeEnum.CropProtection:
                    return "APPLICATION_CROP_PROTECTION";
                case OperationTypeEnum.DataCollection:
                    return "VEHICLE_DATA_COLLECTION_GENERAL";
                case OperationTypeEnum.Fertilizing:
                    return "APPLICATION_FERTILIZING";
                case OperationTypeEnum.ForageHarvesting:
                    return "HARVEST_PRE_HARVEST";
                case OperationTypeEnum.Harvesting:
                    return "HARVEST";
                case OperationTypeEnum.Irrigation:
                    return "APPLICATION_IRRIGATION";
                case OperationTypeEnum.Mowing:
                    return "HARVEST_PRE_HARVEST";
                case OperationTypeEnum.SowingAndPlanting:
                    return "APPLICATION_SOWING_AND_PLANTING";
                case OperationTypeEnum.Swathing:
                    return "HARVEST_PRE_HARVEST";
                case OperationTypeEnum.Tillage:
                    return "FIELD_PREPARATION_TILLAGE";
                case OperationTypeEnum.Transport:
                    return "VEHICLE_DATA_COLLECTION_GENERAL";
                case OperationTypeEnum.Unknown:
                    return "UNKNOWN";
                case OperationTypeEnum.Wrapping:
                    return "HARVEST_POST_HARVEST";
            }
            return null;
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