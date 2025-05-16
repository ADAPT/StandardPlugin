using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Common;
using AgGateway.ADAPT.ApplicationDataModel.Equipment;
using AgGateway.ADAPT.ApplicationDataModel.FieldBoundaries;
using AgGateway.ADAPT.ApplicationDataModel.Guidance;
using AgGateway.ADAPT.ApplicationDataModel.Logistics;
using AgGateway.ADAPT.ApplicationDataModel.Notes;
using AgGateway.ADAPT.ApplicationDataModel.Products;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using AgGateway.ADAPT.Standard;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class CatalogExporter
    {
        private Standard.Catalog _catalog;
        private List<IError> _errors;
        private readonly CommonExporters _commonExporters;

        internal const string UnknownFarmID = "UnknownFarm";
        internal const string UnknownGrowerId = "UnknownGrower";   
        internal const string UnknownFieldId = "UnknownField";

        private CatalogExporter(Root root)
        {
            _catalog = root.Catalog;

            _catalog.Seasons = new List<SeasonElement>();
            _catalog.CustomDataTypeDefinitions = new List<CustomDataTypeDefinitionElement>();
            _catalog.Parties = new List<PartyElement>();
            _errors = new List<IError>();

            _commonExporters = new CommonExporters(root);
        }

        public static IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel dataModel, Root exportRoot, Properties properties = null)
        {
            var exporter = new CatalogExporter(exportRoot);
            return exporter.Export(dataModel);
        }

        private IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel dataModel)
        {
            ExportGrowers(dataModel);
            ExportFarms(dataModel);
            ExportFields(dataModel);
            ExportFieldBoundaries(dataModel.Catalog);
            ExportCrops(dataModel.Catalog);
            ExportCropZones(dataModel.Catalog);
            ExportCompanies(dataModel.Catalog.Companies, dataModel.Catalog.ContactInfo);
            ExportPersons(dataModel.Catalog.Persons, dataModel.Catalog.ContactInfo);
            ExportBrands(dataModel.Catalog);
            ExportDeviceModels(dataModel.Catalog);
            ExportDevices(dataModel.Catalog.DeviceElements, dataModel.Catalog.DeviceModels);
            ExportGuidanceGroups(dataModel.Catalog.GuidanceGroups);
            ExportGuidancePatterns(dataModel.Catalog.GuidancePatterns);
            ExportManufacturers(dataModel.Catalog.Manufacturers);
            ExportProducts(dataModel.Catalog);

            _catalog.Description = dataModel.Catalog.Description;

            //Null these out if empty to avoid empty lists in the output
            if (!_catalog.Parties.Any())
            {
                _catalog.Parties = null;
            }
            if (!_catalog.CustomDataTypeDefinitions.Any())
            {
                _catalog.CustomDataTypeDefinitions = null;
            }
            if (!_catalog.Seasons.Any())
            {
                _catalog.Seasons = null;
            }
            
            _errors.AddRange(_commonExporters.Errors);
            return _errors;
        }

        private void ExportDevices(List<ApplicationDataModel.Equipment.DeviceElement> srcDeviceElements, List<DeviceModel> srcDeviceModels)
        {
            if (srcDeviceElements.IsNullOrEmpty())
            {
                return;
            }

            List<Standard.DeviceElement> output = new List<Standard.DeviceElement>();
            foreach (var frameworkDeviceElement in srcDeviceElements)
            {
                var srcDeviceModel = srcDeviceModels.FirstOrDefault(x => x.Id.ReferenceId == frameworkDeviceElement.ParentDeviceId);
                if (srcDeviceModel == null && frameworkDeviceElement.ParentDeviceId != 0)
                {
                    continue;
                }

                Standard.DeviceElement device = new Standard.DeviceElement()
                {
                    Id = _commonExporters.ExportID(frameworkDeviceElement.Id),
                    Name = frameworkDeviceElement.Description,
                    DeviceModelId = frameworkDeviceElement.DeviceModelId.ToString(CultureInfo.InvariantCulture),
                    SerialNumber = frameworkDeviceElement.SerialNumber,
                    ContextItems = _commonExporters.ExportContextItems(frameworkDeviceElement.ContextItems)
                };
                output.Add(device);
            }
            _catalog.Devices = output.Any() ? output : null;
        }

        private void ExportProducts(ApplicationDataModel.ADM.Catalog srcCatalog)
        {
            if (srcCatalog.Products.IsNullOrEmpty())
            {
                return;
            }

            List<ProductElement> output = new List<ProductElement>();
            foreach (var frameworkProduct in srcCatalog.Products)
            {
                ProductElement product = new ProductElement()
                {
                    Id = _commonExporters.ExportID(frameworkProduct.Id),
                    Name = frameworkProduct.Description,
                    BrandId = srcCatalog.Brands.Any(x => x.Id.ReferenceId == frameworkProduct.BrandId) ? frameworkProduct.BrandId?.ToString(CultureInfo.InvariantCulture) : null,
                    Density = _commonExporters.ExportAsNumericValue<Density>(frameworkProduct.Density),
                    ManufacturerId = srcCatalog.Manufacturers.Any(x => x.Id.ReferenceId == frameworkProduct.ManufacturerId) ? frameworkProduct.ManufacturerId?.ToString(CultureInfo.InvariantCulture) : null,
                    ProductFormCode = ExportProductForm(frameworkProduct.Form),
                    ProductStatusCode = ExportProductStatus(frameworkProduct.Status),
                    ProductTypeCode = ExportProductType(frameworkProduct),
                    SpecificGravity = frameworkProduct.SpecificGravity,
                    ContextItems = _commonExporters.ExportContextItems(frameworkProduct.ContextItems)
                };
                product.ProductComponents = ExportProductComponents(frameworkProduct.ProductComponents, srcCatalog.Ingredients);

                switch (frameworkProduct)
                {
                    case GenericProduct genericProduct:
                        break;
                    case CropNutritionProduct nutritionProduct:
                        product.CropNutritionProductAttributes = new CropNutritionProductAttributes
                        {
                            Ingredients = ExportIngredients(nutritionProduct.ProductComponents, srcCatalog.Ingredients),
                            IsManure = nutritionProduct.IsManure
                        };
                        break;
                    case CropProtectionProduct protectionProduct:
                        product.CropProtectionProductAttributes = new CropProtectionProductAttributes
                        {
                            //The framework didn't have nullable bools and defaulted to false
                            //As such we will treat false as null and only convert true.
                            HasBiological = protectionProduct.Biological ? true : (bool?)null,
                            HasCarbamate = protectionProduct.Carbamate? true : (bool?)null,
                            HasOrganophosphate = protectionProduct.Organophosphate ? true : (bool?)null,
                            Ingredients = ExportIngredients(protectionProduct.ProductComponents, srcCatalog.Ingredients)
                        };
                        break;
                    case CropVarietyProduct varietyProduct:
                        product.CropVarietyProductAttributes = new CropVarietyProductAttributes
                        {
                            CropId = varietyProduct.CropId.ToString(CultureInfo.InvariantCulture),
                            VarietyIsGeneticallyEnhanced = varietyProduct.GeneticallyEnhanced ? true : (bool?)null,
                        };
                        break;
                    case HarvestedCommodityProduct commodityProduct:
                        product.HarvestedProductAttributes = new HarvestedProductAttributes
                        {
                            CropId = commodityProduct.CropId.ToString(CultureInfo.InvariantCulture)
                        };
                        break;
                    case MixProduct mixProduct:
                        product.MixProductAttributes = new MixProductAttributes
                        {
                            MixTotalQuantity = _commonExporters.ExportAsNumericValue<MixTotalQuantity>(mixProduct.TotalQuantity)
                        };
                        product.ProductTypeCode = "MIX";
                        break;
                }
                output.Add(product);
            }
            _catalog.Products = output;
        }

        private List<IngredientElement> ExportIngredients(List<ProductComponent> srcProductComponents, List<Ingredient> srcIngredients)
        {
            var output = new List<IngredientElement>();
            if (srcProductComponents.IsNullOrEmpty())
            {
                return output;
            }
            foreach (var frameworkComponent in srcProductComponents)
            {
                var frameworkIngredient = srcIngredients.FirstOrDefault(x => x.Id.ReferenceId == frameworkComponent.IngredientId);
                if (frameworkIngredient == null)
                {
                    continue;
                }

                var ingredient = new IngredientElement
                {
                    Id = _commonExporters.ExportID(frameworkIngredient.Id),
                    Name = frameworkIngredient.Description,
                    ContextItems = _commonExporters.ExportContextItems(frameworkIngredient.ContextItems)
                };

                switch (frameworkIngredient)
                {
                    case ActiveIngredient activeIngredient:
                        ingredient.IsActiveIngredient = true;
                        break;
                    case InertIngredient inertIngredient:
                        ingredient.IsActiveIngredient = false;
                        break;
                    case CropNutritionIngredient nutritionIngredient:
                        ingredient.CropNutritionIngredientItemCode = ExportNutritionIngredientCode(nutritionIngredient.IngredientCode);
                        ingredient.IsActiveIngredient = true;
                        break;
                }

                output.Add(ingredient);
            }

            return output;
        }

        private string ExportNutritionIngredientCode(EnumeratedValue ingredientCode)
        {
            var code = ingredientCode?.Value?.Value?.ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            switch (code)
            {
                case "DTIN":
                    return "NITROGEN";
                case "DTIP":
                    return "PHOSPHORUS";
                case "DTIK":
                    return "POTASSIUM";
                case "DTICA":
                    return "CALCIUM";
                case "DTIMG":
                    return "MAGNESIUM";
                case "DTIS":
                    return "SULPHUR";
                case "DTIB":
                    return "BORON";
                case "DTICL":
                    return "CHLORINE";
                case "DTICU":
                    return "COPPER";
                case "DTIFE":
                    return "IRON";
                case "DTIMN":
                    return "MANGANESE";
                case "DTIMO":
                    return "MOLYBDENUM";
                case "DTIZN":
                    return "ZINC";
                case "DTIFULVICACID":
                    return "FULVIC_ACID";
                case "DTIHUMICACID":
                    return "HUMIC_ACID";
            }

            return null;
        }

        private List<ProductComponentElement> ExportProductComponents(List<ProductComponent> srcProductComponents, List<Ingredient> srcIngredients)
        {
            if (srcProductComponents.IsNullOrEmpty())
            {
                return null;
            }

            List<ProductComponentElement> output = new List<ProductComponentElement>();
            foreach (var frameworkProductComponent in srcProductComponents)
            {
                var productComponent = new ProductComponentElement
                {
                    IsCarrier = frameworkProductComponent.IsCarrier,
                    MixOrder = frameworkProductComponent.MixOrder,
                    ProductId = CreateProductFromIngredient(frameworkProductComponent, srcIngredients),
                    Quantity = _commonExporters.ExportAsNumericValue<Quantity>(frameworkProductComponent.Quantity)
                };

                output.Add(productComponent);
            }

            return output;
        }

        private string CreateProductFromIngredient(ProductComponent srcProductComponent, List<Ingredient> srcIngredients)
        {
            if (srcProductComponent.IsProduct)
            {
                return srcProductComponent.IngredientId.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }

        private string ExportProductType(Product srcProduct)
        {
            var category = srcProduct.Category;
            var srcType = srcProduct.ProductType;
            bool isManure = false;
            if (srcProduct is CropNutritionProduct fertilizer)
            {
                isManure = fertilizer.IsManure;
            }
            
            switch (category)
            {
                case CategoryEnum.Additive:
                    return "NOT_SPECIFIED";
                case CategoryEnum.Adjuvant:
                    return "SPRAY_ADJUVANT";
                case CategoryEnum.Carrier:
                    break;
                case CategoryEnum.Defoliant:
                    return "DEFOLIANT";
                case CategoryEnum.Fertilizer:
                    return "FERTILIZER_CHEMICAL";
                case CategoryEnum.Fungicide:
                    return "FUNGICIDE";
                case CategoryEnum.GrowthRegulator:
                    return "GROWTH_REGULATOR";
                case CategoryEnum.Herbicide:
                    return "HERBICIDE";
                case CategoryEnum.Insecticide:
                    return "INSECTICIDE";
                case CategoryEnum.Manure:
                    return "FERTILIZER_ORGANIC";
                case CategoryEnum.NitrogenStabilizer:
                    return "NITROGEN_STABILIZER";
                case CategoryEnum.Pesticide:
                    return "NOT_SPECIFIED";
                case CategoryEnum.Unknown:
                    return "NOT_SPECIFIED";
                case CategoryEnum.Variety:
                    return "SEED";
            }
            switch (srcType)
            {
                case ProductTypeEnum.Fertilizer:
                    if (isManure)
                    {
                        return "FERTILIZER_ORGANIC";
                    }
                    else
                    {
                        return "FERTILIZER_CHEMICAL";
                    }
                case ProductTypeEnum.Mix:
                    return "MIX";
                case ProductTypeEnum.Variety:
                    return "SEED";
            }
            return "NOT_SPECIFIED"; 
        }

        private string ExportProductStatus(ProductStatusEnum status)
        {
            switch (status)
            {
                case ProductStatusEnum.Active:
                    return "ACTIVE";
                default:
                    return "INACTIVE";
            }
        }

        private string ExportProductForm(ProductFormEnum form)
        {
            switch (form)
            {
                case ProductFormEnum.Gas:
                    return "GAS";
                case ProductFormEnum.Liquid:
                    return "LIQUID";
                case ProductFormEnum.Solid:
                    return "SOLID";
                default:
                    return "UNKNOWN";
            }
        }

        private void ExportManufacturers(List<Manufacturer> srcManufacturers)
        {
            if (srcManufacturers.IsNullOrEmpty())
            {
                return;
            }

            List<ManufacturerElement> output = new List<ManufacturerElement>();
            foreach (var frameworkManufacturer in srcManufacturers)
            {
                ManufacturerElement manufacturer = new ManufacturerElement()
                {
                    Id = _commonExporters.ExportID(frameworkManufacturer.Id),
                    Name = frameworkManufacturer.Description,
                    ContextItems = _commonExporters.ExportContextItems(frameworkManufacturer.ContextItems)
                };
                output.Add(manufacturer);
            }
            _catalog.Manufacturers = output;
        }

        private void ExportGuidancePatterns(List<GuidancePattern> srcGuidancePatterns)
        {
            if (srcGuidancePatterns.IsNullOrEmpty())
            {
                return;
            }

            List<GuidancePatternElement> output = new List<GuidancePatternElement>();
            foreach (var frameworkGuidancePattern in srcGuidancePatterns)
            {
                GuidancePatternElement guidancePattern = new GuidancePatternElement()
                {
                    Id = _commonExporters.ExportID(frameworkGuidancePattern.Id),
                    Description = frameworkGuidancePattern.Description,
                    GNssSource = ExportGpsSource(frameworkGuidancePattern.GpsSource),
                    GuidancePatternTypeCode = ExportGuidancePatternType(frameworkGuidancePattern.GuidancePatternType),
                    GuidancePatternPropagationDirectionCode = ExportPropagationDirection(frameworkGuidancePattern.PropagationDirection),
                    GuidancePatternExtensionCode = ExportGuidanceExtension(frameworkGuidancePattern.Extension),
                    SwathWidth = _commonExporters.ExportAsNumericValue<SwathWidth>(frameworkGuidancePattern.SwathWidth),
                    NumberOfSwathsLeft = frameworkGuidancePattern.NumbersOfSwathsLeft,
                    NumberOfSwathsRight = frameworkGuidancePattern.NumbersOfSwathsRight,
                    BoundaryGeometry = GeometryExporter.ExportMultiPolygon(frameworkGuidancePattern.BoundingPolygon)
                };

                switch (frameworkGuidancePattern)
                {
                    case AbCurve abCurve:
                        guidancePattern.ABCurveAttributes = new ABCurveAttributes
                        {
                            Heading = abCurve.Heading,
                            NumberOfSegments = abCurve.NumberOfSegments,
                            LineStrings = GeometryExporter.ExportLineStrings(abCurve.Shape)
                        };
                        break;
                    case AbLine abLine:
                        guidancePattern.ABLineAttributes = new ABLineAttributes
                        {
                            A = GeometryExporter.ExportPoint(abLine.A),
                            B = GeometryExporter.ExportPoint(abLine.B),
                            Heading = abLine.Heading
                        };
                        break;
                    case APlus aPlus:
                        guidancePattern.APlusAttributes = new APlusAttributes
                        {
                            A = GeometryExporter.ExportPoint(aPlus.Point),
                            Heading = aPlus.Heading
                        };
                        break;
                    case MultiAbLine multiAbLine:
                        guidancePattern.ABLineAttributes = new ABLineAttributes
                        {
                            A = GeometryExporter.ExportPoint(multiAbLine.AbLines.FirstOrDefault()?.A),
                            B = GeometryExporter.ExportPoint(multiAbLine.AbLines.FirstOrDefault()?.B),
                            Heading = multiAbLine.AbLines.FirstOrDefault()?.Heading
                        };
                        break;
                    case PivotGuidancePattern pivotGuidance:
                        guidancePattern.PivotAttributes = new PivotAttributes
                        {
                            CenterPoint = GeometryExporter.ExportPoint(pivotGuidance.Center),
                            EndPoint = GeometryExporter.ExportPoint(pivotGuidance.EndPoint),
                            StartPoint = GeometryExporter.ExportPoint(pivotGuidance.StartPoint),
                            Radius = _commonExporters.ExportAsNumericValue<Radius>(pivotGuidance.Radius)
                        };

                        if(pivotGuidance.Center == null &&
                           pivotGuidance.Point1 != null &&
                           pivotGuidance.Point2 != null &&
                           pivotGuidance.Point3 != null)
                        {
                            throw new NotImplementedException("PivotGuidancePattern with three points is not implemented.");
                        }
                        break;
                    case Spiral spiral:
                        guidancePattern.SpiralAttributes = new SpiralAttributes
                        {
                            LineStrings = GeometryExporter.ExportLineString(spiral.Shape)
                        };
                        break;
                }
                output.Add(guidancePattern);
            }
            _catalog.GuidancePatterns = output;
        }

        private string ExportGuidanceExtension(GuidanceExtensionEnum extension)
        {
            switch (extension)
            {
                case GuidanceExtensionEnum.FromA:
                    return "FROM_A";
                case GuidanceExtensionEnum.FromB:
                    return "FROM_B";
                case GuidanceExtensionEnum.FromBothPoints:
                    return "FROM_BOTH_POINTS";
            }
            return "NONE";
        }

        private string ExportPropagationDirection(PropagationDirectionEnum propagationDirection)
        {
            switch (propagationDirection)
            {
                case PropagationDirectionEnum.BothDirections:
                    return "BOTH_DIRECTIONS";
                case PropagationDirectionEnum.LeftOnly:
                    return "LEFT_ONLY";
                case PropagationDirectionEnum.NoPropagation:
                    return "NO_PROPAGATION";
                case PropagationDirectionEnum.RightOnly:
                    return "RIGHT_ONLY";
            }
            return null;
        }

        private string ExportGuidancePatternType(GuidancePatternTypeEnum guidancePatternType)
        {
            switch (guidancePatternType)
            {
                case GuidancePatternTypeEnum.AbCurve:
                    return "CURVE";
                case GuidancePatternTypeEnum.AbLine:
                    return "AB_LINE";
                case GuidancePatternTypeEnum.APlus:
                    return "A+";
                case GuidancePatternTypeEnum.CenterPivot:
                    return "CENTER_PIVOT";
                case GuidancePatternTypeEnum.Spiral:
                    return "SPIRAL";
            }
            return null;
        }

        private void ExportGuidanceGroups(List<GuidanceGroup> srcGuidanceGroups)
        {
            if (srcGuidanceGroups.IsNullOrEmpty())
            {
                return;
            }

            List<GuidanceGroupElement> output = new List<GuidanceGroupElement>();
            foreach (var frameworkGuidanceGroup in srcGuidanceGroups)
            {
                GuidanceGroupElement guidanceGroup = new GuidanceGroupElement()
                {
                    Id = _commonExporters.ExportID(frameworkGuidanceGroup.Id),
                    Description = frameworkGuidanceGroup.Description,
                    GuidancePatternIds = frameworkGuidanceGroup.GuidancePatternIds?.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList(),
                    BoundaryGeometry = GeometryExporter.ExportMultiPolygon(frameworkGuidanceGroup.BoundingPolygon)
                };
                output.Add(guidanceGroup);
            }
            _catalog.GuidanceGroups = output;
        }

        private void ExportDeviceModels(ApplicationDataModel.ADM.Catalog srcCatalog)
        {
            if (srcCatalog.DeviceModels.IsNullOrEmpty())
            {
                return;
            }

            List<DeviceModelElement> output = new List<DeviceModelElement>();
            foreach (var frameworkDeviceModel in srcCatalog.DeviceModels)
            {
                var series = srcCatalog.DeviceSeries.FirstOrDefault(x => x.Id.ReferenceId == frameworkDeviceModel.SeriesId);
                DeviceModelElement deviceModel = new DeviceModelElement()
                {
                    Id = _commonExporters.ExportID(frameworkDeviceModel.Id),
                    Name = frameworkDeviceModel.Description,
                    BrandId = GetIdWithReferentialIntegrity(srcCatalog.Brands, frameworkDeviceModel.BrandId),
                    DeviceSeries = series?.Description,
                    ContextItems = _commonExporters.ExportContextItems(frameworkDeviceModel.ContextItems)
                };
                output.Add(deviceModel);
            }
            _catalog.DeviceModels = output;
        }

        private void ExportBrands(ApplicationDataModel.ADM.Catalog srcCatalog)
        {
            if (srcCatalog.Brands.IsNullOrEmpty())
            {
                return;
            }

            List<BrandElement> output = new List<BrandElement>();
            foreach (var frameworkBrand in srcCatalog.Brands)
            {
                BrandElement brand = new BrandElement()
                {
                    Id = _commonExporters.ExportID(frameworkBrand.Id),
                    Name = frameworkBrand.Description,
                    ManufacturerId = GetIdWithReferentialIntegrity(srcCatalog.Manufacturers, frameworkBrand.ManufacturerId),
                    ContextItems = _commonExporters.ExportContextItems(frameworkBrand.ContextItems)
                };
                output.Add(brand);
            }
            _catalog.Brands = output;
        }

        private string GetIdWithReferentialIntegrity<T>(List<T> values, int? id)
        {
            if (id == null)
            {
                return null;
            }
            foreach (dynamic item in values)
            {
                if (item.Id.ReferenceId == id)
                {
                    return item.Id.ReferenceId.ToString(CultureInfo.InvariantCulture);
                }
            }
            return null;
        }

        private void ExportPersons(List<Person> srcPersons, List<ADAPT.ApplicationDataModel.Logistics.ContactInfo> srcContactInfos)
        {
            if (srcPersons.IsNullOrEmpty())
            {
                return;
            }

            List<PartyElement> output = new List<PartyElement>();
            foreach (var frameworkPerson in srcPersons)
            {
                PartyElement party = new PartyElement()
                {
                    Id = _commonExporters.ExportID(frameworkPerson.Id),
                    Name = !string.IsNullOrWhiteSpace(frameworkPerson.CombinedName)
                        ? frameworkPerson.CombinedName
                        : string.Join(" ", Extensions.FilterEmptyValues(frameworkPerson.FirstName, frameworkPerson.MiddleName, frameworkPerson.LastName)),
                    PartyTypeCode = "INDIVIDUAL",
                    ContactInfo = ExportContactInfo(srcContactInfos.FirstOrDefault(x => x.Id.ReferenceId == frameworkPerson.ContactInfoId)),
                    ContextItems = _commonExporters.ExportContextItems(frameworkPerson.ContextItems)
                };
                output.Add(party);
            }

            _catalog.Parties.AddRange(output);
        }

        private void ExportCompanies(List<Company> srcCompanies, List<ApplicationDataModel.Logistics.ContactInfo> srcContactInfos)
        {
            if (srcCompanies.IsNullOrEmpty())
            {
                return;
            }

            List<PartyElement> output = new List<PartyElement>();
            foreach (var frameworkCompany in srcCompanies)
            {
                PartyElement party = new PartyElement()
                {
                    Id = _commonExporters.ExportID(frameworkCompany.Id),
                    Name = frameworkCompany.Name,
                    PartyTypeCode = "BUSINESS",
                    ContactInfo = ExportContactInfo(srcContactInfos.FirstOrDefault(x => x.Id.ReferenceId == frameworkCompany.ContactInfoId)),
                    ContextItems = _commonExporters.ExportContextItems(frameworkCompany.ContextItems)
                };
                output.Add(party);
            }
            _catalog.Parties = _catalog.Parties ?? new List<PartyElement>();
            _catalog.Parties.AddRange(output);
        }

        private void ExportCropZones(ApplicationDataModel.ADM.Catalog srcCatalog)
        {
            if (srcCatalog.CropZones.IsNullOrEmpty())
            {
                return;
            }

            List<CropZoneElement> output = new List<CropZoneElement>();
            foreach (var frameworkCropZone in srcCatalog.CropZones)
            {
                CropZoneElement cropZone = new CropZoneElement()
                {
                    Id = _commonExporters.ExportID(frameworkCropZone.Id),
                    Name = frameworkCropZone.Description,
                    ArableArea = _commonExporters.ExportAsNumericValue<ArableArea>(frameworkCropZone.Area),
                    CropId = GetIdWithReferentialIntegrity(srcCatalog.Crops, frameworkCropZone.CropId),
                    FieldId = GetIdWithReferentialIntegrity(srcCatalog.Fields, frameworkCropZone.FieldId),
                    GNssSource = ExportGpsSource(frameworkCropZone.BoundarySource),
                    GuidanceGroupIds = frameworkCropZone.GuidanceGroupIds.Any() ? frameworkCropZone.GuidanceGroupIds?.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList() : null,
                    Notes = ExportNotes(frameworkCropZone.Notes),
                    TimeScopes = _commonExporters.ExportTimeScopes(frameworkCropZone.TimeScopes, out var seasonIds),
                    SeasonIds = seasonIds,
                    BoundaryGeometry = GeometryExporter.ExportMultiPolygon(frameworkCropZone.BoundingRegion),
                    ContextItems = _commonExporters.ExportContextItems(frameworkCropZone.ContextItems)
                };
                output.Add(cropZone);
            }
            _catalog.CropZones = output;
        }

        private List<string> ExportNotes(List<Note> srcNotes)
        {
            if (srcNotes.IsNullOrEmpty())
            {
                return null;
            }

            List<string> output = new List<string>();
            foreach (var frameworkNote in srcNotes)
            {
                var note = string.Join(" ", Extensions.FilterEmptyValues(frameworkNote.Description, frameworkNote.Value?.Value.Value));
                output.Add(note);
            }
            return output;
        }

        private void ExportCrops(ApplicationDataModel.ADM.Catalog srcCatalog)
        {
            if (srcCatalog.Crops.IsNullOrEmpty())
            {
                return;
            }

            List<CropElement> output = new List<CropElement>();
            foreach (var frameworkCrop in srcCatalog.Crops)
            {
                CropElement crop = new CropElement()
                {
                    Id = _commonExporters.ExportID(frameworkCrop.Id),
                    Name = frameworkCrop.Name,
                    ParentId = GetIdWithReferentialIntegrity(srcCatalog.Crops, frameworkCrop.ParentId),
                    ReferenceWeight = _commonExporters.ExportAsNumericValue<ReferenceWeight>(frameworkCrop.ReferenceWeight),
                    StandardPayableMoisture = _commonExporters.ExportAsNumericValue<StandardPayableMoisture>(frameworkCrop.StandardPayableMoisture),
                    ContextItems = _commonExporters.ExportContextItems(frameworkCrop.ContextItems)
                };
                output.Add(crop);
            }
            _catalog.Crops = output;
        }

        private void ExportFieldBoundaries(ApplicationDataModel.ADM.Catalog srcCatalog)
        {
            if (srcCatalog.FieldBoundaries.IsNullOrEmpty())
            {
                return;
            }

            List<FieldBoundaryElement> output = new List<FieldBoundaryElement>();
            foreach (var frameworkFieldBoundary in srcCatalog.FieldBoundaries)
            {
                FieldBoundaryElement fieldBoundary = new FieldBoundaryElement()
                {
                    Id = _commonExporters.ExportID(frameworkFieldBoundary.Id),
                    Name = frameworkFieldBoundary.Description,
                    FieldId = GetIdWithReferentialIntegrity(srcCatalog.Fields, frameworkFieldBoundary.FieldId),
                    GNssSource = ExportGpsSource(frameworkFieldBoundary.GpsSource),
                    Headlands = ExportHeadlands(frameworkFieldBoundary.Headlands),
                    Geometry = GeometryExporter.ExportMultiPolygon(frameworkFieldBoundary.SpatialData, frameworkFieldBoundary.InteriorBoundaryAttributes?.Select(x => x.Shape)),
                    SeasonIds = ExportTimeScopesAsSeasons(frameworkFieldBoundary.TimeScopes)?.Select(x => x.Id.ReferenceId).ToList(),
                    ContextItems = _commonExporters.ExportContextItems(frameworkFieldBoundary.ContextItems),
                };
                output.Add(fieldBoundary);
            }
            _catalog.FieldBoundaries = output;
        }

        private List<SeasonElement> ExportTimeScopesAsSeasons(List<TimeScope> srcTimeScopes)
        {
            if (srcTimeScopes.IsNullOrEmpty())
            {
                return null;
            }

            List<SeasonElement> output = new List<SeasonElement>();
            foreach (var frameworkTimeScope in srcTimeScopes.Where(x => x.DateContext == DateContextEnum.CropSeason))
            {
                output.Add(_commonExporters.GetOrAddSeason(frameworkTimeScope));
            }
            return output;
        }

        private bool SrcModelHasLoggedData(ApplicationDataModel.ADM.ApplicationDataModel srcModel)
        {
            if (srcModel.Documents?.LoggedData == null)
            {
                return false;
            }
            else
            {
                return srcModel.Documents.LoggedData.Any();
            }
        }

        private void ExportFields(ApplicationDataModel.ADM.ApplicationDataModel srcModel)
        {
            List<FieldElement> output = new List<FieldElement>();
            if (srcModel.Catalog.Fields != null)
            {
                foreach (var frameworkField in srcModel.Catalog.Fields)
                {
                    FieldElement field = new FieldElement()
                    {
                        Id = _commonExporters.ExportID(frameworkField.Id),
                        Name = frameworkField.Description,
                        FarmId = GetIdWithReferentialIntegrity(srcModel.Catalog.Farms, frameworkField.FarmId),
                        ArableArea = _commonExporters.ExportAsNumericValue<ArableArea>(frameworkField.Area),
                        ActiveBoundaryId = GetIdWithReferentialIntegrity(srcModel.Catalog.FieldBoundaries, frameworkField.ActiveBoundaryId),
                        GuidanceGroupIds = frameworkField.GuidanceGroupIds.Any() ? frameworkField.GuidanceGroupIds?.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList() : null,
                        ContextItems = _commonExporters.ExportContextItems(frameworkField.ContextItems)
                    };
                    output.Add(field);
                }
            }
            if (!output.Any() && SrcModelHasLoggedData(srcModel))
            {
                FieldElement field = new FieldElement()
                {
                    Name = "Unknown Field",
                    FarmId = UnknownFarmID,
                    Id = new Id() { ReferenceId = UnknownFieldId },
                };
                output.Add(field);
            }
            _catalog.Fields = output;
        }

        private void ExportFarms(ApplicationDataModel.ADM.ApplicationDataModel srcModel)
        {
            List<FarmElement> output = new List<FarmElement>();
            if (srcModel.Catalog.Farms != null)
            {
                foreach (var frameworkFarm in srcModel.Catalog.Farms)
                {
                    FarmElement farm = new FarmElement()
                    {
                        Id = _commonExporters.ExportID(frameworkFarm.Id),
                        Name = frameworkFarm.Description,
                        GrowerId = GetIdWithReferentialIntegrity(srcModel.Catalog.Growers, frameworkFarm.GrowerId),
                        ContextItems = _commonExporters.ExportContextItems(frameworkFarm.ContextItems),
                        PartyId = ExportContactInfo(frameworkFarm.ContactInfo, frameworkFarm.Description)
                    };
                    output.Add(farm);
                }
            }
            if (!output.Any() && SrcModelHasLoggedData(srcModel))
            {
                 FarmElement farm = new FarmElement()
                    {
                        Name = "Unknown Farm",
                        GrowerId = UnknownGrowerId,
                         Id = new Id() { ReferenceId = UnknownFarmID },
                    };
                    output.Add(farm);
            }
            _catalog.Farms = output;
        }

        private void ExportGrowers(ApplicationDataModel.ADM.ApplicationDataModel srcModel)
        {
            List<GrowerElement> output = new List<GrowerElement>();
            if (srcModel.Catalog.Growers != null)
            {
                foreach (var frameworkGrower in srcModel.Catalog.Growers)
                {
                    GrowerElement grower = new GrowerElement()
                    {
                        Name = frameworkGrower.Name,
                        Id = _commonExporters.ExportID(frameworkGrower.Id),
                        ContextItems = _commonExporters.ExportContextItems(frameworkGrower.ContextItems),
                        PartyId = ExportContactInfo(frameworkGrower.ContactInfo, frameworkGrower.Name)
                    };
                    output.Add(grower);
                }
            }
            if (!output.Any() && SrcModelHasLoggedData(srcModel))
            {
                 GrowerElement grower = new GrowerElement()
                    {
                        Name = "Unknown Grower",
                        Id = new Id() { ReferenceId = UnknownGrowerId },
                    };
                    output.Add(grower);
            }

            _catalog.Growers = output;
        }

        private List<HeadlandElement> ExportHeadlands(List<Headland> srcHeadlands)
        {
            if (srcHeadlands.IsNullOrEmpty())
            {
                return null;
            }

            List<HeadlandElement> output = new List<HeadlandElement>();
            foreach (var frameworkHeadland in srcHeadlands)
            {
                HeadlandElement headland = new HeadlandElement()
                {
                    Name = frameworkHeadland.Description
                };
                if (frameworkHeadland is DrivenHeadland drivenHeadland)
                {
                    headland.Geometry = GeometryExporter.ExportMultiPolygon(drivenHeadland.SpatialData);
                }
                else if (frameworkHeadland is ConstantOffsetHeadland)
                {
                    _errors.Add(new Error
                    {
                        Description = "Discarding ConstantOffsetHeadland",
                    });
                }

                output.Add(headland);
            }
            return output;
        }

        private GNssSource ExportGpsSource(GpsSource gpsSource)
        {
            if (gpsSource == null)
            {
                return null;
            }

            return new GNssSource
            {
                EstimatedPrecision = _commonExporters.ExportAsNumericValue<EstimatedPrecision>(gpsSource.EstimatedPrecision),
                HorizontalAccuracy = _commonExporters.ExportAsNumericValue<HorizontalAccuracy>(gpsSource.HorizontalAccuracy),
                VerticalAccuracy = _commonExporters.ExportAsNumericValue<VerticalAccuracy>(gpsSource.VerticalAccuracy),
                GNssutcTime = gpsSource.GpsUtcTime?.ToString("O", CultureInfo.InvariantCulture),
                NumberOfSatellites = gpsSource.NumberOfSatellites,
            };
        }

        private string ExportContactInfo(ApplicationDataModel.Logistics.ContactInfo contactInfo, string ownerName)
        {
            if (contactInfo == null)
            {
                return null;
            }

            var party = new PartyElement
            {
                Id = _commonExporters.ExportID(contactInfo.Id),
                Name = ownerName,
                PartyTypeCode = "UNKNOWN",
                ContactInfo = ExportContactInfo(contactInfo)
            };

            _catalog.Parties.Add(party);
            return party.Id.ReferenceId;
        }

        private Standard.ContactInfo ExportContactInfo(ApplicationDataModel.Logistics.ContactInfo srcContactInfo)
        {
            if (srcContactInfo == null)
            {
                return null;
            }

            Standard.ContactInfo contactInfo = new Standard.ContactInfo();
            var addressLines = Extensions.FilterEmptyValues(srcContactInfo.AddressLine1, srcContactInfo.AddressLine2, srcContactInfo.PoBoxNumber);
            if (addressLines.Any())
            {
                contactInfo.AddressContactMethods = new List<AddressContactMethodElement>
                    {
                        new AddressContactMethodElement
                        {
                            AddressContactTypeCode = "POSTAL",
                            AddressLines = addressLines.Any() ? addressLines : null,
                            City = srcContactInfo.City,
                            Country = srcContactInfo.Country,
                            CountryCode = srcContactInfo.CountryCode,
                            PostalCode =  srcContactInfo.PostalCode,
                        }
                    };
                contactInfo.ContextItems = _commonExporters.ExportContextItems(srcContactInfo.ContextItems);
            }

            if (!srcContactInfo.Contacts.IsNullOrEmpty())
            {
                contactInfo.TelecommunicationContactMethods = new List<TelecommunicationContactMethodElement>();
                foreach (var frameworkContact in srcContactInfo.Contacts)
                {
                    contactInfo.TelecommunicationContactMethods.Add(new TelecommunicationContactMethodElement
                    {
                        TelecommunicationContactTypeCode = ExportContactType(frameworkContact.Type),
                        ValueText = frameworkContact.Number
                    });
                }
            }
            return null;
        }

        private string ExportContactType(ContactTypeEnum contactType)
        {
            switch (contactType)
            {
                case ContactTypeEnum.Email:
                    return "EMAIL";
                case ContactTypeEnum.Fax:
                    return "FAX";
                case ContactTypeEnum.FixedPhone:
                    return "FIXED_PHONE";
                case ContactTypeEnum.MobilePhone:
                    return "MOBILE_PHONE";
            }
            return null;
        }
    }
}