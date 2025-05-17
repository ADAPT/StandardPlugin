using System.Collections.Generic;
using System.Linq;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Common;
using AgGateway.ADAPT.ApplicationDataModel.Documents;
using AgGateway.ADAPT.ApplicationDataModel.Prescriptions;
using AgGateway.ADAPT.ApplicationDataModel.Products;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using AgGateway.ADAPT.ApplicationDataModel.Shapes;
using NUnit.Framework;

namespace StandardPluginTests
{
    public class PrescriptionTests
    {
        private ApplicationDataModel _adm;

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void ManualRx()
        {
            SetupProducts();
            var field = new AgGateway.ADAPT.ApplicationDataModel.Logistics.Field() { Description = "Test Field" };
            _adm.Catalog.Fields = new List<AgGateway.ADAPT.ApplicationDataModel.Logistics.Field>()
            {
                field
            };

            ProductUse use = new ProductUse()
            {
                Rate = new NumericRepresentationValue() { Representation = new NumericRepresentation() { Code = "vrAppRateMassActual" }, Value = new NumericValue(new UnitOfMeasure() { Code = "lb1ac-1" }, 15d) },
                ProductId = _adm.Catalog.Products.First().Id.ReferenceId
            };
            ManualPrescription rx = new ManualPrescription();
            rx.ProductUses.Add(use);
            _adm.Catalog.Prescriptions = new List<ManualPrescription>() { rx };

                        
            var plugin = new AgGateway.ADAPT.StandardPlugin.Plugin();
            plugin.Export(_adm, TestContext.CurrentContext.WorkDirectory);

        }

        [Test]
        public void MultiProductVectorPrescription()
        {
            SetupProducts();

            var field = new AgGateway.ADAPT.ApplicationDataModel.Logistics.Field() { Description = "Test Field" };
            _adm.Catalog.Fields = new List<AgGateway.ADAPT.ApplicationDataModel.Logistics.Field>()
            {
                field
            };


            RxProductLookup p1 = new RxProductLookup()
            {
                ProductId = _adm.Catalog.Products.First().Id.ReferenceId,
                Representation = new NumericRepresentation() { Code = "vrSeedRateSeedsActual" },
                UnitOfMeasure = new UnitOfMeasure() { Code = "count1ac-1" }
            };
            RxProductLookup p4 = new RxProductLookup()
            {
                ProductId = _adm.Catalog.Products.Last().Id.ReferenceId,
                Representation = new NumericRepresentation() { Code = "vrAppRateVolumeActual" },
                UnitOfMeasure = new UnitOfMeasure() { Code = "gal1ac-1" }
            };
            VectorPrescription rx = new VectorPrescription() { FieldId = field.Id.ReferenceId, RxProductLookups = new List<RxProductLookup>() { p1, p4}};

            Polygon one = new Polygon()
            {
                ExteriorRing = new LinearRing()
                {
                    Points = new List<Point>()
                    {
                        new Point { X = -87.60, Y = 41.88  },
                        new Point { X = -87.60, Y = 41.881  },
                        new Point { X = -87.599, Y = 41.881  },
                        new Point { X = -87.599, Y = 41.88  },
                        new Point { X = -87.60, Y = 41.88  },
                    }
                }
            };

            Polygon two = new Polygon()
            {
                ExteriorRing = new LinearRing()
                {
                    Points = new List<Point>()
                    {
                        new Point { X = -87.60, Y = 41.879  },
                        new Point { X = -87.60, Y = 41.88  },
                        new Point { X = -87.599, Y = 41.88  },
                        new Point { X = -87.599, Y = 41.879  },
                        new Point { X = -87.60, Y = 41.879  },
                    }
                }
            };

            rx.RxShapeLookups.Add(new RxShapeLookup()
            {
                Shape = new MultiPolygon() { Polygons = new List<Polygon>() { one } },
                Rates = new List<RxRate>()
                {
                    new RxRate(){ Rate = 33000, RxProductLookupId = p1.Id.ReferenceId},
                    new RxRate(){ Rate = 100, RxProductLookupId = p4.Id.ReferenceId},
                }
            });
            rx.RxShapeLookups.Add(new RxShapeLookup()
            {
                Shape = new MultiPolygon() { Polygons = new List<Polygon>() { two } },
                Rates = new List<RxRate>()
                {
                    new RxRate(){ Rate = 35500, RxProductLookupId = p1.Id.ReferenceId},
                    new RxRate(){ Rate = 75, RxProductLookupId = p4.Id.ReferenceId},
                }
            });

            WorkItemOperation op = new WorkItemOperation()
            {
                OperationType = OperationTypeEnum.SowingAndPlanting,
                PrescriptionId = rx.Id.ReferenceId
            };
            _adm.Documents.WorkItemOperations = new List<WorkItemOperation>() { op };


            WorkItem item = new WorkItem()
            {
                WorkItemOperationIds = new List<int>() { op.Id.ReferenceId },
                FieldId = field.Id.ReferenceId
            };
            _adm.Documents.WorkItems = new List<WorkItem>() { item };
            _adm.Catalog.Prescriptions = new List<VectorPrescription>() { rx };

           
            
            var plugin = new AgGateway.ADAPT.StandardPlugin.Plugin();
            plugin.Export(_adm, TestContext.CurrentContext.WorkDirectory);

        }

        [Test]
        public void MultiProductRasterPrescription()
        {
            SetupProducts();

            var rxPrescription = SetupRasterPrescription();
            for (int h = 0; h < rxPrescription.RowCount; h++)
            {
                for (int w = 0; w < rxPrescription.ColumnCount; w++)
                {
                    var rates = rxPrescription.RxProductLookups.Select((x, i) => new RxRate
                    {
                        Rate = h * rxPrescription.ColumnCount + w + i * 100,
                        RxProductLookupId = x.Id.ReferenceId
                    }).ToList();
                    rxPrescription.Rates.Add(new RxCellLookup { RxRates = rates });
                }
            }
            _adm.Catalog.Prescriptions = new List<Prescription> { rxPrescription };

            var plugin = new AgGateway.ADAPT.StandardPlugin.Plugin();
            plugin.Export(_adm, TestContext.CurrentContext.WorkDirectory);
        }

        private RasterGridPrescription SetupRasterPrescription()
        {
           return new RasterGridPrescription
            {
                CellHeight = new NumericRepresentationValue { Value = new NumericValue(new UnitOfMeasure(), 0.001) },
                CellWidth = new NumericRepresentationValue { Value = new NumericValue(new UnitOfMeasure(), 0.002) },
                ColumnCount = 10,
                RowCount = 5,
                OperationType = OperationTypeEnum.SowingAndPlanting,
                Origin = new Point { X = -87.60, Y = 41.88  },
                RxProductLookups = _adm.Catalog.Products.Select(x => new RxProductLookup { ProductId = x.Id.ReferenceId, UnitOfMeasure = new UnitOfMeasure { Code = "kg1ha-1" }, Representation = new NumericRepresentation() { Code = "vrAppRateMassActual"} }).ToList(),
                ProductIds = _adm.Catalog.Products.Select(x => x.Id.ReferenceId).ToList(),
                Rates = new List<RxCellLookup>(),
                Description = "RasterRx"
            };
        }

        private void SetupProducts()
        {
            _adm = new ApplicationDataModel
            {
                Catalog = new Catalog
                {
                    Products = new List<Product>
                    {
                        new GenericProduct { Description = "Product 1", Category = CategoryEnum.Fertilizer, ProductType = ProductTypeEnum.Fertilizer, Form = ProductFormEnum.Solid, Status = ProductStatusEnum.Active },
                        new GenericProduct { Description = "Product 2", Category = CategoryEnum.Fungicide, ProductType = ProductTypeEnum.Chemical, Form = ProductFormEnum.Liquid, Status = ProductStatusEnum.Active },
                        new GenericProduct { Description = "Product 3", Category = CategoryEnum.Herbicide, ProductType = ProductTypeEnum.Chemical, Form = ProductFormEnum.Liquid, Status = ProductStatusEnum.Active },
                        new GenericProduct { Description = "Product 4", Category = CategoryEnum.Insecticide, ProductType = ProductTypeEnum.Chemical, Form = ProductFormEnum.Liquid, Status = ProductStatusEnum.Active }
                    },
                },
                Documents = new Documents(),
            };

        }
    }
}