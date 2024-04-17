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

        private CatalogExporter(Root root)
        {
            _catalog = root.Catalog;

            _catalog.Seasons = new List<SeasonElement>();
            _catalog.CustomDataTypeDefinitions = new List<CustomDataTypeDefinitionElement>();
            _catalog.Parties = new List<PartyElement>();
            _errors = new List<IError>();
        }

        public static IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel dataModel, Root exportRoot, Properties properties = null)
        {
            var exporter = new CatalogExporter(exportRoot);
            return exporter.Export(dataModel);
        }

        private IEnumerable<IError> Export(ApplicationDataModel.ADM.ApplicationDataModel dataModel)
        {
            ExportGrowers(dataModel.Catalog.Growers);
            ExportFarms(dataModel.Catalog.Farms);
            ExportFields(dataModel.Catalog.Fields);
            ExportFieldBoundaries(dataModel.Catalog.FieldBoundaries);
            ExportCrops(dataModel.Catalog.Crops);
            ExportCropZones(dataModel.Catalog.CropZones);
            ExportCompanies(dataModel.Catalog.Companies, dataModel.Catalog.ContactInfo);
            ExportPersons(dataModel.Catalog.Persons, dataModel.Catalog.ContactInfo);
            ExportBrands(dataModel.Catalog.Brands);
            ExportDeviceModels(dataModel.Catalog.DeviceModels, dataModel.Catalog.DeviceSeries);
            ExportDevices(dataModel.Catalog.DeviceElements);
            ExportGuidanceGroups(dataModel.Catalog.GuidanceGroups);
            ExportGuidancePatterns(dataModel.Catalog.GuidancePatterns);
            ExportManufacturers(dataModel.Catalog.Manufacturers);
            ExportProducts(dataModel.Catalog.Products, dataModel.Catalog.Ingredients);

            _catalog.Description = dataModel.Catalog.Description;

            if (!_catalog.Parties.Any())
            {
                _catalog.Parties = null;
            }
            return _errors;
        }

        private void ExportDevices(List<ApplicationDataModel.Equipment.DeviceElement> srcDeviceElements)
        {
            if (srcDeviceElements.IsNullOrEmpty())
            {
                return;
            }

            List<Standard.DeviceElement> output = new List<Standard.DeviceElement>();
            foreach (var frameworkDeviceElement in srcDeviceElements)
            {
                if (frameworkDeviceElement.DeviceElementType != DeviceElementTypeEnum.Machine)
                {
                    continue;
                }

                Standard.DeviceElement device = new Standard.DeviceElement()
                {
                    Id = ExportID(frameworkDeviceElement.Id),
                    Description = frameworkDeviceElement.Description,
                    DeviceModelId = frameworkDeviceElement.DeviceModelId.ToString(CultureInfo.InvariantCulture),
                    SerialNumber = frameworkDeviceElement.SerialNumber,
                    ContextItems = ExportContextItem(frameworkDeviceElement.ContextItems)
                };
                output.Add(device);
            }
            _catalog.Devices = output.Any() ? output : null;
        }

        private void ExportProducts(List<Product> srcProducts, List<Ingredient> srcIngredients)
        {
            if (srcProducts.IsNullOrEmpty())
            {
                return;
            }

            List<ProductElement> output = new List<ProductElement>();
            foreach (var frameworkProduct in srcProducts)
            {
                ProductElement product = new ProductElement()
                {
                    Id = ExportID(frameworkProduct.Id),
                    Description = frameworkProduct.Description,
                    BrandId = frameworkProduct.BrandId?.ToString(CultureInfo.InvariantCulture),
                    Density = ExportAsNumericValue<Density>(frameworkProduct.Density),
                    ManufacturerId = frameworkProduct.ManufacturerId?.ToString(CultureInfo.InvariantCulture),
                    ProductFormCode = ExportProductForm(frameworkProduct.Form),
                    ProductStatusCode = ExportProductStatus(frameworkProduct.Status),
                    ProductTypeCode = ExportProductType(frameworkProduct.ProductType, frameworkProduct.Category),
                    SpecificGravity = frameworkProduct.SpecificGravity,
                    ContextItems = ExportContextItem(frameworkProduct.ContextItems)
                };
                product.ProductComponents = ExportProductComponents(frameworkProduct.ProductComponents, srcIngredients);

                switch (frameworkProduct)
                {
                    case GenericProduct genericProduct:
                        break;
                    case CropNutritionProduct nutritionProduct:
                        product.CropNutritionProductAttributes = new CropNutritionProductAttributes
                        {
                            Ingredients = ExportIngredients(nutritionProduct.ProductComponents, srcIngredients),
                            IsManure = nutritionProduct.IsManure
                        };
                        break;
                    case CropProtectionProduct protectionProduct:
                        product.CropProtectionProductAttributes = new CropProtectionProductAttributes
                        {
                            HasBiological = protectionProduct.Biological,
                            HasCarbamate = protectionProduct.Carbamate,
                            HasOrganophosphate = protectionProduct.Organophosphate,
                            Ingredients = ExportIngredients(protectionProduct.ProductComponents, srcIngredients)
                        };
                        break;
                    case CropVarietyProduct varietyProduct:
                        product.CropVarietyProductAttributes = new CropVarietyProductAttributes
                        {
                            CropId = varietyProduct.CropId.ToString(CultureInfo.InvariantCulture),
                            VarietyIsGeneticallyEnhanced = varietyProduct.GeneticallyEnhanced
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
                            MixTotalQuantity = ExportAsNumericValue<MixTotalQuantity>(mixProduct.TotalQuantity)
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
            foreach (var frameworkComponent in srcProductComponents)
            {
                var frameworkIngredient = srcIngredients.FirstOrDefault(x => x.Id.ReferenceId == frameworkComponent.IngredientId);
                if (frameworkIngredient == null)
                {
                    continue;
                }

                var ingredient = new IngredientElement
                {
                    Id = ExportID(frameworkIngredient.Id),
                    Description = frameworkIngredient.Description,
                    ContextItems = ExportContextItem(frameworkIngredient.ContextItems)
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
                case "NITROGEN":
                case "PHOSPHORUS":
                case "POTASSIUM":
                case "CALCIUM":
                case "MAGNESIUM":
                case "SULPHUR":
                case "BORON":
                case "CHLORINE":
                case "COPPER":
                case "IRON":
                case "MANGANESE":
                case "MOLYBDENUM":
                case "ZINC":
                case "FULVIC_ACID":
                case "HUMIC_ACID":
                    return code;
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
                    Quantity = ExportAsNumericValue<Quantity>(frameworkProductComponent.Quantity)
                };
            }

            return output;
        }

        private string CreateProductFromIngredient(ProductComponent srcProductComponent, List<Ingredient> srcIngredients)
        {
            if (srcProductComponent.IsProduct)
            {
                return srcProductComponent.IngredientId.ToString(CultureInfo.InvariantCulture);
            }

            var srcIngredient = srcIngredients.FirstOrDefault(x => x.Id.ReferenceId == srcProductComponent.IngredientId);
            if (srcIngredient == null)
            {
                return null;
            }

            var product = new ProductElement
            {
                Id = ExportID(srcIngredient.Id),
                Description = srcIngredient.Description,
                ProductStatusCode = ExportProductStatus(srcIngredient is ActiveIngredient ? ProductStatusEnum.Active : ProductStatusEnum.Inactive),
                ProductFormCode = ExportProductForm(ProductFormEnum.Unknown),
                ProductTypeCode = ExportProductType(ProductTypeEnum.Generic, CategoryEnum.Unknown),
                ContextItems = ExportContextItem(srcIngredient.ContextItems)
            };

            _catalog.Products.Add(product);
            return product.Id.ReferenceId;
        }

        private string ExportProductType(ProductTypeEnum productType, CategoryEnum category)
        {
            switch (category)
            {
                case CategoryEnum.Additive:
                    break;
                case CategoryEnum.Adjuvant:
                    return "SPRAY_ADJUVANT";
                case CategoryEnum.Carrier:
                    break;
                case CategoryEnum.Defoliant:
                    return "DEFOLIANT";
                case CategoryEnum.Fertilizer:
                    break;
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
                case CategoryEnum.Unknown:
                case CategoryEnum.Variety:
                    break;
            }

            switch (productType)
            {
                case ProductTypeEnum.Chemical:
                    return "FERTILIZER_CHEMICAL";
                case ProductTypeEnum.Fertilizer:
                    break;
                case ProductTypeEnum.Mix:
                    return "MIX";
                case ProductTypeEnum.Variety:
                    return "SEED";
                default:
                    return "NOT_SPECIFIED";
            }
            return null;
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
                    Id = ExportID(frameworkManufacturer.Id),
                    Description = frameworkManufacturer.Description,
                    ContextItems = ExportContextItem(frameworkManufacturer.ContextItems)
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
                    Id = ExportID(frameworkGuidancePattern.Id),
                    Description = frameworkGuidancePattern.Description,
                    GNssSource = ExportGpsSource(frameworkGuidancePattern.GpsSource),
                    GuidancePatternTypeCode = ExportGuidancePatternType(frameworkGuidancePattern.GuidancePatternType),
                    GuidancePatternPropagationDirectionCode = ExportPropagationDirection(frameworkGuidancePattern.PropagationDirection),
                    GuidancePatternExtensionCode = ExportGuidanceExtension(frameworkGuidancePattern.Extension),
                    SwathWidth = ExportAsNumericValue<SwathWidth>(frameworkGuidancePattern.SwathWidth),
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
                            Radius = ExportAsNumericValue<Radius>(pivotGuidance.Radius)
                        };

                        GeometryExporter.ThreePointsToCenterRadius(pivotGuidance.Point1, pivotGuidance.Point2, pivotGuidance.Point3, out var centerPoint, out var radius);
                        if (centerPoint != null)
                        {
                            guidancePattern.PivotAttributes.CenterPoint = centerPoint;
                            guidancePattern.PivotAttributes.Radius = radius;
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
                    Id = ExportID(frameworkGuidanceGroup.Id),
                    Description = frameworkGuidanceGroup.Description,
                    GuidancePatternIds = frameworkGuidanceGroup.GuidancePatternIds?.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList(),
                    BoundaryGeometry = GeometryExporter.ExportMultiPolygon(frameworkGuidanceGroup.BoundingPolygon)
                };
                output.Add(guidanceGroup);
            }
            _catalog.GuidanceGroups = output;
        }

        private void ExportDeviceModels(List<DeviceModel> srcDeviceModels, List<DeviceSeries> srcDeviceSeries)
        {
            if (srcDeviceModels.IsNullOrEmpty())
            {
                return;
            }

            List<DeviceModelElement> output = new List<DeviceModelElement>();
            foreach (var frameworkDeviceModel in srcDeviceModels)
            {
                var series = srcDeviceSeries.FirstOrDefault(x => x.Id.ReferenceId == frameworkDeviceModel.SeriesId);
                DeviceModelElement deviceModel = new DeviceModelElement()
                {
                    Id = ExportID(frameworkDeviceModel.Id),
                    Description = frameworkDeviceModel.Description,
                    BrandId = frameworkDeviceModel.BrandId.ToString(CultureInfo.InvariantCulture),
                    DeviceSeries = series?.Description,
                    ContextItems = ExportContextItem(frameworkDeviceModel.ContextItems)
                };
                output.Add(deviceModel);
            }
            _catalog.DeviceModels = output;
        }

        private void ExportBrands(List<Brand> srcBrands)
        {
            if (srcBrands.IsNullOrEmpty())
            {
                return;
            }

            List<BrandElement> output = new List<BrandElement>();
            foreach (var frameworkBrand in srcBrands)
            {
                BrandElement brand = new BrandElement()
                {
                    Id = ExportID(frameworkBrand.Id),
                    Description = frameworkBrand.Description,
                    ManufacturerId = frameworkBrand.ManufacturerId.ToString(CultureInfo.InvariantCulture),
                    ContextItems = ExportContextItem(frameworkBrand.ContextItems)
                };
                output.Add(brand);
            }
            _catalog.Brands = output;
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
                    Id = ExportID(frameworkPerson.Id),
                    Name = !string.IsNullOrWhiteSpace(frameworkPerson.CombinedName)
                        ? frameworkPerson.CombinedName
                        : string.Join(" ", Extensions.FilterEmptyValues(frameworkPerson.FirstName, frameworkPerson.MiddleName, frameworkPerson.LastName)),
                    PartyTypeCode = "INDIVIDUAL",
                    ContactInfo = ExportContactInfo(srcContactInfos.FirstOrDefault(x => x.Id.ReferenceId == frameworkPerson.ContactInfoId)),
                    ContextItems = ExportContextItem(frameworkPerson.ContextItems)
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
                    Id = ExportID(frameworkCompany.Id),
                    Name = frameworkCompany.Name,
                    PartyTypeCode = "BUSINESS",
                    ContactInfo = ExportContactInfo(srcContactInfos.FirstOrDefault(x => x.Id.ReferenceId == frameworkCompany.ContactInfoId)),
                    ContextItems = ExportContextItem(frameworkCompany.ContextItems)
                };
                output.Add(party);
            }
            _catalog.Parties = _catalog.Parties ?? new List<PartyElement>();
            _catalog.Parties.AddRange(output);
        }

        private void ExportCropZones(List<CropZone> srcCropZones)
        {
            if (srcCropZones.IsNullOrEmpty())
            {
                return;
            }

            List<CropZoneElement> output = new List<CropZoneElement>();
            foreach (var frameworkCropZone in srcCropZones)
            {
                CropZoneElement cropZone = new CropZoneElement()
                {
                    Id = ExportID(frameworkCropZone.Id),
                    Name = frameworkCropZone.Description,
                    ArableArea = ExportAsNumericValue<ArableArea>(frameworkCropZone.Area),
                    CropId = frameworkCropZone.CropId?.ToString(CultureInfo.InvariantCulture),
                    FieldId = frameworkCropZone.FieldId.ToString(CultureInfo.InvariantCulture),
                    GNssSource = ExportGpsSource(frameworkCropZone.BoundarySource),
                    GuidanceGroupIds = frameworkCropZone.GuidanceGroupIds?.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList(),
                    Notes = ExportNotes(frameworkCropZone.Notes),
                    TimeScopes = ExportTimeScopes(frameworkCropZone.TimeScopes),
                    BoundaryGeometry = GeometryExporter.ExportMultiPolygon(frameworkCropZone.BoundingRegion),
                    ContextItems = ExportContextItem(frameworkCropZone.ContextItems)
                };
                output.Add(cropZone);
            }
            _catalog.CropZones = output;
        }

        private List<TimeScopeElement> ExportTimeScopes(List<TimeScope> srcTimeScopes)
        {
            if (srcTimeScopes.IsNullOrEmpty())
            {
                return null;
            }

            List<TimeScopeElement> output = new List<TimeScopeElement>();
            foreach (var frameworkTimeScope in srcTimeScopes)
            {
                if (frameworkTimeScope.DateContext == DateContextEnum.TimingEvent)
                {
                    _errors.Add(new Error
                    {
                        Description = "Discarding TimingEVent TimeScope",
                        Id = frameworkTimeScope.Id.ReferenceId.ToString(CultureInfo.InvariantCulture),
                    });
                    continue;
                }

                var timeScope = new TimeScopeElement
                {
                    DateContextCode = ExportDateContext(frameworkTimeScope.DateContext),
                    Duration = frameworkTimeScope.Duration?.TotalSeconds,
                    Start = frameworkTimeScope.TimeStamp1?.ToString("O", CultureInfo.InvariantCulture),
                    End = frameworkTimeScope.TimeStamp2?.ToString("O", CultureInfo.InvariantCulture),
                };
                output.Add(timeScope);

                if (frameworkTimeScope.DateContext == DateContextEnum.CropSeason)
                {
                    _catalog.Seasons.Add(new SeasonElement
                    {
                        Id = ExportID(frameworkTimeScope.Id),
                        Description = frameworkTimeScope.Description,
                        Start = timeScope.Start,
                        End = timeScope.End,
                    });
                }

            }
            return output;
        }

        private string ExportDateContext(DateContextEnum dateContext)
        {
            switch (dateContext)
            {
                case DateContextEnum.ActualEnd:
                case DateContextEnum.ActualShipping:
                case DateContextEnum.ActualStart:
                    return "ACTUAL";
                case DateContextEnum.Approval:
                    return "APPROVAL";
                case DateContextEnum.Calibration:
                    return "CALIBRATION";
                case DateContextEnum.Creation:
                    return "CREATION";
                case DateContextEnum.Expiration:
                    return "EXPIRATION";
                case DateContextEnum.Installation:
                    return "INSTALLATION";
                case DateContextEnum.Load:
                    return "LOAD";
                case DateContextEnum.Maintenance:
                    return "MAINTENANCE";
                case DateContextEnum.Modification:
                    return "MODIFICATION";
                case DateContextEnum.PhenomenonTime:
                    return "PHENOMENON_TIME";
                case DateContextEnum.ProposedEnd:
                case DateContextEnum.ProposedStart:
                    return "PROPOSED";
                case DateContextEnum.RequestedEnd:
                case DateContextEnum.RequestedStart:
                    return "REQUESTED";
                case DateContextEnum.RequestedShipping:
                    return "REQUESTED_SHIPPING";
                case DateContextEnum.ResultTime:
                    return "RESULT_TIME";
                case DateContextEnum.Resume:
                    return "RESUME";
                case DateContextEnum.Suspend:
                    return "SUSPEND";
                case DateContextEnum.Unload:
                    return "UNLOAD";
                case DateContextEnum.Unspecified:
                    return "UNSPECIFIED";
                case DateContextEnum.ValidityRange:
                    return "VALIDITY";
                default:
                    return null;
            }

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

        private void ExportCrops(List<Crop> srcCrops)
        {
            if (srcCrops.IsNullOrEmpty())
            {
                return;
            }

            List<CropElement> output = new List<CropElement>();
            foreach (var frameworkCrop in srcCrops)
            {
                CropElement crop = new CropElement()
                {
                    Id = ExportID(frameworkCrop.Id),
                    Name = frameworkCrop.Name,
                    ParentId = frameworkCrop.ParentId?.ToString(CultureInfo.InvariantCulture),
                    ReferenceWeight = ExportAsNumericValue<ReferenceWeight>(frameworkCrop.ReferenceWeight),
                    StandardPayableMoisture = ExportAsNumericValue<StandardPayableMoisture>(frameworkCrop.StandardPayableMoisture),
                    ContextItems = ExportContextItem(frameworkCrop.ContextItems)
                };
                output.Add(crop);
            }
            _catalog.Crops = output;
        }

        private void ExportFieldBoundaries(List<FieldBoundary> srcFieldBoundaries)
        {
            if (srcFieldBoundaries.IsNullOrEmpty())
            {
                return;
            }

            List<FieldBoundaryElement> output = new List<FieldBoundaryElement>();
            foreach (var frameworkFieldBoundary in srcFieldBoundaries)
            {
                FieldBoundaryElement fieldBoundary = new FieldBoundaryElement()
                {
                    Id = ExportID(frameworkFieldBoundary.Id),
                    Name = frameworkFieldBoundary.Description,
                    FieldId = frameworkFieldBoundary.FieldId.ToString(CultureInfo.InvariantCulture),
                    GNssSource = ExportGpsSource(frameworkFieldBoundary.GpsSource),
                    Headlands = ExportHeadlands(frameworkFieldBoundary.Headlands),
                    Geometry = GeometryExporter.ExportMultiPolygon(frameworkFieldBoundary.SpatialData),
                    ContextItems = ExportContextItem(frameworkFieldBoundary.ContextItems)
                };
                output.Add(fieldBoundary);
            }
            _catalog.FieldBoundaries = output;
        }

        private void ExportFields(List<Field> srcFields)
        {
            if (srcFields.IsNullOrEmpty())
            {
                return;
            }

            List<FieldElement> output = new List<FieldElement>();
            foreach (var frameworkField in srcFields)
            {
                FieldElement grower = new FieldElement()
                {
                    Id = ExportID(frameworkField.Id),
                    Name = frameworkField.Description,
                    FarmId = frameworkField.FarmId?.ToString(CultureInfo.InvariantCulture),
                    ArableArea = ExportAsNumericValue<ArableArea>(frameworkField.Area),
                    ActiveBoundaryId = frameworkField.ActiveBoundaryId?.ToString(CultureInfo.InvariantCulture),
                    GuidanceGroupIds = frameworkField.GuidanceGroupIds?.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList(),
                    ContextItems = ExportContextItem(frameworkField.ContextItems)
                };
                output.Add(grower);
            }
            _catalog.Fields = output;
        }

        private void ExportFarms(List<Farm> srcFarms)
        {
            if (srcFarms.IsNullOrEmpty())
            {
                return;
            }

            List<FarmElement> output = new List<FarmElement>();
            foreach (var frameworkFarm in srcFarms)
            {
                FarmElement grower = new FarmElement()
                {
                    Id = ExportID(frameworkFarm.Id),
                    Name = frameworkFarm.Description,
                    GrowerId = frameworkFarm.GrowerId?.ToString(CultureInfo.InvariantCulture),
                    ContextItems = ExportContextItem(frameworkFarm.ContextItems),
                    PartyId = ExportContactInfo(frameworkFarm.ContactInfo, frameworkFarm.Description)
                };
                output.Add(grower);
            }
            _catalog.Farms = output;
        }

        private void ExportGrowers(IEnumerable<Grower> srcGrowers)
        {
            if (srcGrowers.IsNullOrEmpty())
            {
                return;
            }

            List<GrowerElement> output = new List<GrowerElement>();
            foreach (var frameworkGrower in srcGrowers)
            {
                GrowerElement grower = new GrowerElement()
                {
                    Name = frameworkGrower.Name,
                    Id = ExportID(frameworkGrower.Id),
                    ContextItems = ExportContextItem(frameworkGrower.ContextItems),
                    PartyId = ExportContactInfo(frameworkGrower.ContactInfo, frameworkGrower.Name)
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
                EstimatedPrecision = ExportAsNumericValue<EstimatedPrecision>(gpsSource.EstimatedPrecision),
                HorizontalAccuracy = ExportAsNumericValue<HorizontalAccuracy>(gpsSource.HorizontalAccuracy),
                VerticalAccuracy = ExportAsNumericValue<VerticalAccuracy>(gpsSource.VerticalAccuracy),
                GNssutcTime = gpsSource.GpsUtcTime?.ToString("O", CultureInfo.InvariantCulture),
                NumberOfSatellites = gpsSource.NumberOfSatellites,
            };
        }

        public static Id ExportID(CompoundIdentifier srcId)
        {
            var id = new Id { ReferenceId = srcId.ReferenceId.ToString(CultureInfo.InvariantCulture) };

            var ids = new List<UniqueIdElement>();
            foreach (var srcUniqueId in srcId.UniqueIds)
            {
                var uniqueId = new UniqueIdElement
                {
                    IdText = srcUniqueId.Id,
                    IdSource = string.IsNullOrEmpty(srcUniqueId.Source) ? srcUniqueId.Source : null,
                };
                switch (srcUniqueId.IdType)
                {
                    case IdTypeEnum.UUID: uniqueId.IdTypeCode = "UUID"; break;
                    case IdTypeEnum.LongInt: uniqueId.IdTypeCode = "LONGINT"; break;
                    case IdTypeEnum.String: uniqueId.IdTypeCode = "STRING"; break;
                    case IdTypeEnum.URI: uniqueId.IdTypeCode = "URI"; break;
                }
                switch (srcUniqueId.SourceType)
                {
                    case IdSourceTypeEnum.URI: uniqueId.IdSourceTypeCode = "URI"; break;
                    case IdSourceTypeEnum.GLN: uniqueId.IdSourceTypeCode = "GLN"; break;
                }
                ids.Add(uniqueId);
            }
            if (ids.Any())
            {
                id.UniqueIds = ids;
            }
            return id;
        }

        private string ExportContactInfo(ApplicationDataModel.Logistics.ContactInfo contactInfo, string ownerName)
        {
            if (contactInfo == null)
            {
                return null;
            }

            var party = new PartyElement
            {
                Id = ExportID(contactInfo.Id),
                Name = ownerName,
                PartyTypeCode = "UNKNOWN",
                ContactInfo = ExportContactInfo(contactInfo)
            };

            _catalog.Parties.Add(party);
            return party.Id.ReferenceId;
        }

        private Standard.ContactInfo ExportContactInfo(ApplicationDataModel.Logistics.ContactInfo srcContactInfo)
        {
            var addressLines = Extensions.FilterEmptyValues(srcContactInfo.AddressLine1, srcContactInfo.AddressLine2, srcContactInfo.PoBoxNumber);
            var contactInfo = new Standard.ContactInfo
            {
                AddressContactMethods = new List<AddressContactMethodElement>
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
                    },
                ContextItems = ExportContextItem(srcContactInfo.ContextItems)
            };

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

            return contactInfo;
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

        private List<ContextItemElement> ExportContextItem(List<ContextItem> srcContextItems, int level = 1)
        {
            if (srcContextItems.IsNullOrEmpty())
            {
                return null;
            }
            if (level > 2)
            {
                _errors.Add(new Error
                {
                    Description = "Discarding nested context items",
                });
                return null;
            }

            var output = new List<ContextItemElement>();
            foreach (var frameworkContextItem in srcContextItems)
            {
                var contextItem = new ContextItemElement
                {
                    ValueText = frameworkContextItem.Value,
                    ContextItems = ExportContextItem(frameworkContextItem.NestedItems, level + 1)
                };

                if (frameworkContextItem.Code.EqualsIgnoreCase("US-EPA-N"))
                {
                    contextItem.DefinitionCode = "US-EPA-RegistrationNumber";
                }
                else
                {
                    contextItem.DefinitionCode = frameworkContextItem.Code;
                    TryAddCustomDataType(frameworkContextItem.Code, frameworkContextItem.ValueUOM, "TEXT", "INVALID");
                }

                output.Add(contextItem);
            }
            return output;
        }

        private void TryAddCustomDataType(string code, string valueUOM, string baseTypeCode, string statusCode)
        {
            var existingCustomDataType = _catalog.CustomDataTypeDefinitions.FirstOrDefault(x => x.DefinitionCode == code);
            if (existingCustomDataType != null)
            {
                return;
            }

            _catalog.CustomDataTypeDefinitions.Add(new CustomDataTypeDefinitionElement
            {
                DefinitionCode = code,
                Name = $"{code} ({valueUOM})",
                DataDefinitionBaseTypeCode = baseTypeCode,
                DataDefinitionStatusCode = statusCode,
            });
        }

        private static T ExportAsNumericValue<T>(NumericRepresentationValue srcRepresentationValue)
            where T : BaseNumericValue
        {
            if (srcRepresentationValue == null)
            {
                return default(T);
            }
            BaseNumericValue output = Activator.CreateInstance<T>();
            output.NumericValue = srcRepresentationValue.Value.Value;
            output.UnitOfMeasureCode = srcRepresentationValue.UserProvidedUnitOfMeasure?.Code ?? srcRepresentationValue.Representation?.Code;

            return (T)output;
        }
    }
}