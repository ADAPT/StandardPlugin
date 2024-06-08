using System.Collections.Generic;
using System.Linq;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Common;
using AgGateway.ADAPT.ApplicationDataModel.Prescriptions;
using AgGateway.ADAPT.ApplicationDataModel.Products;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using AgGateway.ADAPT.ApplicationDataModel.Shapes;
using NUnit.Framework;

namespace StandardPluginTests
{
    public class RasterGridPrescriptionTests
    {
        private ApplicationDataModel _adm;

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void MultiProductRasterPrescription()
        {
            SetupProducts();

            var rxPrescription = SetupPrescription();
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

        private RasterGridPrescription SetupPrescription()
        {
           return new RasterGridPrescription
            {
                CellHeight = new NumericRepresentationValue { Value = new NumericValue(new UnitOfMeasure(), 0.001) },
                CellWidth = new NumericRepresentationValue { Value = new NumericValue(new UnitOfMeasure(), 0.002) },
                ColumnCount = 10,
                RowCount = 5,
                OperationType = OperationTypeEnum.SowingAndPlanting,
                Origin = new Point { X = 50.11, Y = 10.12 },
                RxProductLookups = _adm.Catalog.Products.Select(x => new RxProductLookup { ProductId = x.Id.ReferenceId, UnitOfMeasure = new UnitOfMeasure { } }).ToList(),
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
                }
            };

        }
    }
}