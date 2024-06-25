using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgGateway.ADAPT.ApplicationDataModel.ADM;
using AgGateway.ADAPT.ApplicationDataModel.Common;
using AgGateway.ADAPT.ApplicationDataModel.Logistics;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using AgGateway.ADAPT.Standard;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal class CommonExporters
    {
        private readonly List<IError> _errors;
        private readonly Standard.Catalog _catalog;

        public CommonExporters(Standard.Root root)
        {
            _catalog = root.Catalog;
            _errors = new List<IError>();
        }

        public List<IError> Errors
        {
            get { return _errors; }
        }

        public Id ExportID(CompoundIdentifier srcId)
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

        public List<TimeScopeElement> ExportTimeScopes(List<TimeScope> srcTimeScopes)
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
                        Description = "Discarding TimingEvent TimeScope",
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

        public List<ContextItemElement> ExportContextItems(List<ContextItem> srcContextItems, int level = 1)
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
                    ContextItems = ExportContextItems(frameworkContextItem.NestedItems, level + 1)
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

        public string ExportRole(EnumeratedValue role)
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

        public List<Roo> ExportPersonRoles(List<PersonRole> srcPersonRoles)
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
                    TimeScopes = ExportTimeScopes(frmeworkPersonRole.TimeScopes)
                };
                output.Add(partyRole);
            }
            return output;
        }

        public string ExportOperationType(OperationTypeEnum operationType)
        {
            switch (operationType)
            {
                case OperationTypeEnum.Baling:
                    return "HARVEST";
                case OperationTypeEnum.CropProtection:
                    return "APPLICATION_CROP_PROTECTION";
                case OperationTypeEnum.DataCollection:
                    return "VEHICLE_DATA_COLLECTION_GENERAL";
                case OperationTypeEnum.Fertilizing:
                    return "APPLICATION_FERTILIZING";
                case OperationTypeEnum.ForageHarvesting:
                    return "HARVEST";
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

        private static string ExportDateContext(DateContextEnum dateContext)
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
    }
}
