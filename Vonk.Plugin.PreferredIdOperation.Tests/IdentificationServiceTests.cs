using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Vonk.Core.Common;
using Vonk.Core.Context;
using Vonk.Core.Repository;
using Vonk.Plugin.PreferredIdOperation.Tests.Helpers;
using static Hl7.Fhir.Model.NamingSystem;
using static Vonk.Plugin.PreferredIdOperation.Helpers.LoggerUtils;
using Task = System.Threading.Tasks.Task;

namespace Vonk.Plugin.PreferredIdOperation.Tests
{
    public class IdentificationServiceTests
    {
        private IdentificationService _testOperationService;
        private ILogger<IdentificationService> _logger = Logger<IdentificationService>();
        private Mock<IAdministrationSearchRepository> _mockedAdministrationRepository = new Mock<IAdministrationSearchRepository>();
        public IdentificationServiceTests()
        {
            SetupRepository();
            _testOperationService = new IdentificationService(_logger, _mockedAdministrationRepository.Object);
        }

        private void SetupRepository()
        {
            NamingSystem mockedSystem = new NamingSystem
            {
                Id = "1",
                Kind = NamingSystemType.Identifier,
                UniqueId = new List<UniqueIdComponent> { 
                    new UniqueIdComponent() 
                    { 
                        Type = NamingSystemIdentifierType.Oid,
                        Value = "1.234.5678.90"
                    },
                    new UniqueIdComponent()
                    {
                        Type = NamingSystemIdentifierType.Uri,
                        Value = "http://test.uri.com"
                    }
                }
            };
            var mockedSystemNode = FhirJsonNode.Parse(mockedSystem.ToJson());
            List<IResource> resources = new List<IResource>() { mockedSystemNode.ToIResource(VonkConstants.Model.FhirR4) };
            _mockedAdministrationRepository
                .Setup(repo => repo.Search(It.IsAny<IArgumentCollection>(), It.IsAny<SearchOptions>()))
                .ReturnsAsync(new SearchResult(resources, 1));
        }


        [Fact]
        public async Task TestOperationOK()
        {
            //_mockedAdministrationRepository.Setup(() => Search)

            var testContext = new VonkTestContext(VonkInteraction.type_custom);
            testContext.Arguments.AddArguments(new[]
            {
                new Argument(ArgumentSource.Path, ArgumentNames.resourceType, "NamingSystem", ArgumentStatus.Handled),
                new Argument(ArgumentSource.Query, "id", "1.234.5678.90", ArgumentStatus.Handled),
                new Argument(ArgumentSource.Query, "type", "oid", ArgumentStatus.Handled)
            });
            testContext.TestRequest.CustomOperation = "preferred-id";
            testContext.TestRequest.Method = "GET";

            // Execute $preferred-id
            await _testOperationService.PreferredIdGET(testContext);

            // Check response
            Assert.True(testContext.Response.HttpResult == StatusCodes.Status200OK);
            Assert.True(testContext.Response.Payload.Type.Equals("Parameters"));
        }

        [Fact]
        public async Task TestOperationBadRequestLackOfArguments()
        {
            var testContext = new VonkTestContext(VonkInteraction.type_custom);
            testContext.Arguments.AddArguments(new[]
            {
                new Argument(ArgumentSource.Path, ArgumentNames.resourceType, "NamingSystem"),
            });
            testContext.TestRequest.CustomOperation = "preferred-id";
            testContext.TestRequest.Method = "GET";

            // Execute $preferred-id
            await _testOperationService.PreferredIdGET(testContext);

            // Check response
            Assert.True(testContext.Response.HttpResult == StatusCodes.Status400BadRequest);
            Assert.True(testContext.Response.Payload.Type.Equals("OperationOutcome"));
        }

        [Fact]
        public async Task TestOperationBadRequestWrongResourceType()
        {
            var testContext = new VonkTestContext(VonkInteraction.type_custom);
            testContext.Arguments.AddArguments(new[]
            {
                new Argument(ArgumentSource.Path, ArgumentNames.resourceType, "CodingSystem"),
                new Argument(ArgumentSource.Query, "id", "1.234.5678.90", ArgumentStatus.Handled),
                new Argument(ArgumentSource.Query, "type", "url", ArgumentStatus.Handled)
            });
            testContext.TestRequest.CustomOperation = "preferred-id";
            testContext.TestRequest.Method = "GET";

            // Execute $preferred-id
            await _testOperationService.PreferredIdGET(testContext);

            // Check response
            Assert.True(testContext.Response.HttpResult == StatusCodes.Status400BadRequest);
            Assert.True(testContext.Response.Payload.Type.Equals("OperationOutcome"));
        }

        [Fact]
        public async Task TestOperationNotFound()
        {
            var testContext = new VonkTestContext(VonkInteraction.type_custom);
            testContext.Arguments.AddArguments(new[]
            {
                new Argument(ArgumentSource.Path, ArgumentNames.resourceType, "NamingSystem"),
                new Argument(ArgumentSource.Query, "id", "1234", ArgumentStatus.Handled),
                new Argument(ArgumentSource.Query, "type", "url", ArgumentStatus.Handled)
            });
            testContext.TestRequest.CustomOperation = "preferred-id";
            testContext.TestRequest.Method = "GET";

            // Execute $preferred-id
            await _testOperationService.PreferredIdGET(testContext);

            // Check response
            Assert.True(testContext.Response.HttpResult == StatusCodes.Status404NotFound);
            Assert.True(testContext.Response.Payload.Type.Equals("OperationOutcome"));
        }
    }
}