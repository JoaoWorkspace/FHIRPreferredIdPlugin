using Microsoft.Extensions.Logging;
using Hl7.Fhir.Model;
using Vonk.Core.Context;
using Vonk.Core.Repository;
using Task = System.Threading.Tasks.Task;
using Vonk.Core.Pluggability;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Hl7.Fhir.Rest;
using Hl7.FhirPath.Sprache;
using Hl7.Fhir.ElementModel;
using Vonk.Core.Support;
using static Hl7.Fhir.Model.NamingSystem;
using Vonk.Core.ElementModel;
using static Hl7.Fhir.Model.Parameters;
using Vonk.Core.Common;
using System.Net;
using System.Collections.Generic;
using Hl7.Fhir.Introspection;
using System.Reflection.PortableExecutable;

namespace Vonk.Plugin.PreferredIdOperation
{
    public class IdentificationService
    {
        private readonly ILogger<IdentificationService> _logger;
        private readonly IAdministrationSearchRepository _administrationRepository;
        private readonly FhirJsonSerializationSettings _serializeSettings;
        public IdentificationService(ILogger<IdentificationService> logger, IAdministrationSearchRepository administrationRepository)
        {
            _logger = logger;
            _administrationRepository = administrationRepository;
            _serializeSettings = new FhirJsonSerializationSettings() { Pretty = true };
        }

        /// <summary>
        /// This mapper transforms the input string type into the required NamingSystemIdentifierType
        /// Default to URI because plugin typically allows for strings like URL as well as URI to be parsed into URI IdentifierType
        /// So making that the default made most sense.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private NamingSystemIdentifierType MapTypeToIdentifierType(string type)
        {
            NamingSystemIdentifierType identifierType;
            switch (type)
            {
                case "uuid": identifierType = NamingSystemIdentifierType.Uuid; break;
                case "oid": identifierType = NamingSystemIdentifierType.Oid; break;
                default: identifierType = NamingSystemIdentifierType.Uri; break; //could be uri, could be url, coule be absent.
            }
            return identifierType;
        }

        private async Task<IEnumerable<NamingSystem>> FindFilteredResource(ArgumentCollection arguments, SearchOptions options, NamingSystemIdentifierType type, string knownId)
        {
            /*Filter Repository for NamingSystem*/
            SearchResult result = await _administrationRepository.Search(arguments, options);

            /*Convert resources to POCO type NamingSystem*/
            var pocoResult = result.Select(resource => resource.ToPoco<NamingSystem>()).ToList();
            _logger.LogInformation($"{pocoResult.Count()} results found for NamingSystem");

            /*Filter a specific resource by their known identifier */
            var filteredByKnownId = pocoResult.Where(ns => ns.UniqueId.HasAny(c => c.Value.Equals(knownId)));
            _logger.LogInformation($"{filteredByKnownId.Count()} filtered results found with Id={knownId}");

            /*Checks if the resources with KnownId has any Identifier for the given IdentifierType*/
            var filteredResult = filteredByKnownId.Where(ns => ns.UniqueId.HasAny(c => c.TypeElement.Value == type));
            _logger.LogInformation($"{filteredResult.Count()} filtered results found with Id={knownId} and with an IdentifierType of {type}");

            return filteredResult;
        }

        private Parameters BuildParametersFromNamingSystem(NamingSystem namingSystemResource, NamingSystemIdentifierType identifierType)
        {
            /*Extracts UniqueIdComponent from the NamingSystem Resource*/
            UniqueIdComponent preferredUniqueIdentifier = namingSystemResource.UniqueId.Single(identifier => identifier.Type.Value == identifierType);

            /*Builds the Parameters to return setting the appropriate values from the UniqueIDComponent*/
            ParameterComponent componentResult = new ParameterComponent();
            componentResult.Name = preferredUniqueIdentifier.Type.ToString();
            componentResult.Value = preferredUniqueIdentifier.ValueElement;
            Parameters parameters = new Parameters()
            {
                Parameter = new List<ParameterComponent>() { componentResult }
            };
            return parameters;
        }

        private IVonkResponse HandleNotFoundResponse(IVonkContext context, NamingSystemIdentifierType targetType, string id)
        {
            _logger.LogInformation($"$Preferred-Id Operation returned without any match.");
            OperationOutcome outcome = new OperationOutcome()
            {
                Issue = new List<OperationOutcome.IssueComponent> { 
                    new OperationOutcome.IssueComponent(){
                        Details = new CodeableConcept(
                            "http://hl7.org/fhir/dotnet-api-operation-outcome",
                            "5000",
                            $"$preferred-id operation found no NamingSystem resources with Identifier=[{id}] and TargetType=[{targetType}]"
                        )
                    }
                }
            };
            context.Response.HttpResult = (int)HttpStatusCode.NotFound;
            SetResponsePayload(outcome.ToJson(_serializeSettings), context);
            return context.Response;
        }

        private IVonkResponse HandleWrongResourceResponse(IVonkContext context, string resource)
        {
            _logger.LogWarning($"$Preferred-Id Operation called with the wrong resource=[{resource}] when NamingSystem is expected.");
            OperationOutcome outcome = new OperationOutcome()
            {
                Issue = new List<OperationOutcome.IssueComponent> {
                    new OperationOutcome.IssueComponent(){
                        Details = new CodeableConcept(
                            "http://hl7.org/fhir/dotnet-api-operation-outcome",
                            "5000",
                            $"Operation called with wrong resource. Expected [NamingSystem] and got [{resource}] instead."
                        )
                    }
                }
            };
            context.Response.HttpResult = (int)HttpStatusCode.BadRequest;
            SetResponsePayload(outcome.ToJson(_serializeSettings), context);
            return context.Response;
        }

        private IVonkResponse NoIdentifierProvidedResponse(IVonkContext context)
        {
            _logger.LogWarning("$Preferred-Id Operation called without an identifier.");
            OperationOutcome outcome = new OperationOutcome()
            {
                Issue = new List<OperationOutcome.IssueComponent> {
                    new OperationOutcome.IssueComponent(){
                        Details = new CodeableConcept(
                            "http://hl7.org/fhir/dotnet-api-operation-outcome",
                            "5000",
                            "No id provided to the $preferred-id operation."
                        ),
                    }
                }
            };
            context.Response.HttpResult = (int) HttpStatusCode.BadRequest;
            SetResponsePayload(outcome.ToJson(_serializeSettings), context);
            return context.Response;
        }

        /// <summary>
        /// Handles the $Preferred-Id GET custom operation and will function even if only a known identifier of the NamingSystem resource
        /// is sent, by defaulting to URI identifiers to return (expecting all namingsystem to have at least one URI identifier, otherwise
        /// the response will come NotFound)
        /// If the known identifier isn't provided (argument "id") then it'll be impossible to single out a single NamingSystem, and an error
        /// response code is returned accordingly.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        [InteractionHandler(VonkInteraction.type_custom, CustomOperation = "preferred-id", Method = "GET", InformationModel = VonkConstants.Model.FhirR4 /*, /AcceptedTypes = new string[] { "NamingSystem" }*/ )]
        public async Task<IVonkResponse> PreferredIdGET(IVonkContext context)
        {
            /*Handle Arguments*/
            var typeArg = context.Arguments.GetArgument("type")?.ArgumentValue ?? "";
            NamingSystemIdentifierType identifierType = MapTypeToIdentifierType(typeArg);
            var idArg = context.Arguments.GetArgument("id")?.ArgumentValue;
            if (idArg == null)
            {
                return NoIdentifierProvidedResponse(context);
            }
            var resourceTypeArg = context.Arguments.GetArgument("_type")?.ArgumentValue;
            if (!resourceTypeArg.Equals("NamingSystem"))
            {
                return HandleWrongResourceResponse(context, resourceTypeArg);
            }
            context.Arguments.Handled();

            /*Call Administration Repository*/
            var arguments = new ArgumentCollection(
              new Argument(ArgumentSource.Path, "_type", resourceTypeArg, ArgumentStatus.Handled)
            );
            var options = SearchOptions.Latest(context.ServerBase, context.Request.Interaction, context.InformationModel);
            var filteredResult = await FindFilteredResource(arguments, options, identifierType, idArg);
            if (filteredResult.Count() < 1)
            {
                return HandleNotFoundResponse(context, identifierType, idArg);
            }

            /*Work data to return a resource*/
            var namingSystem = filteredResult.SingleOrDefault();
            var parametersToReturn = BuildParametersFromNamingSystem(namingSystem, identifierType);

            /*return payload*/
            context.Response.HttpResult = (int) HttpStatusCode.OK;
            SetResponsePayload(parametersToReturn.ToJson(_serializeSettings), context);
            return context.Response;
        }

        private void SetResponsePayload(string json, IVonkContext context)
        {
            var preferredIdResponse = FhirJsonNode.Parse(json);
            context.Response.Payload = preferredIdResponse.ToIResource(VonkConstants.Model.FhirR4);

        }
    }
}