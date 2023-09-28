using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vonk.Core.Context;
using Xunit;
using FluentAssertions;
using static Vonk.UnitTests.Framework.Helpers.LoggerUtils;
using Microsoft.AspNetCore.Http;
using System.Linq;
using static Vonk.Core.Context.VonkOutcome;
using Vonk.UnitTests.Framework.Helpers;
using Vonk.Core.Repository;
using Moq;

namespace Vonk.Plugin.PreferredIdOperation.Tests
{
    public class IdentificationServiceTests
    {
        private IdentificationService _testOperationService;
        private ILogger<IdentificationService> _logger = Logger<IdentificationService>();
        private Mock<IAdministrationSearchRepository> _mockedAdministrationRepository = new Mock<IAdministrationSearchRepository>();
        public IdentificationServiceTests()
        {
            _testOperationService = new IdentificationService(_logger, _mockedAdministrationRepository.Object);
        }

        [Fact]
        public void TestOperationShouldSucceed()
        {
            var testContext = new VonkTestContext(VonkInteraction.type_custom);
            testContext.Arguments.AddArguments(new[]
            {
                new Argument(ArgumentSource.Path, ArgumentNames.resourceType, "NamingSystem"),
                new Argument(ArgumentSource.Query, "id", "1234"),
                new Argument(ArgumentSource.Query, "type", "url")
            });
            testContext.TestRequest.CustomOperation = "preferred-id";
            testContext.TestRequest.Method = "GET";

            // Execute $preferred-id
            _testOperationService.PreferredIdGET(testContext).Wait();

            // Check response
            testContext.Response.HttpResult.Should().Be(StatusCodes.Status200OK, "$test should succeed");
            testContext.Response.Outcome.Issues.Count().Should().Be(1, "An OperationOutcome should be included in the response");
            testContext.Response.Outcome.IssuesWithSeverity(IssueSeverity.Information).Count().Should().Be(1, "The OperationOutcome should be informational, not an error");
        }

        [Fact]
        public void TestOperationShouldNotSucceed()
        {
            var testContext = new VonkTestContext(VonkInteraction.type_custom);
            testContext.Arguments.AddArguments(new[]
            {
                new Argument(ArgumentSource.Path, ArgumentNames.resourceType, "NamingSystem"),
            });
            testContext.TestRequest.CustomOperation = "test";
            testContext.TestRequest.Method = "GET";

            // Execute $preferred-id
            _testOperationService.PreferredIdGET(testContext).Wait();

            // Check response
            testContext.Response.HttpResult.Should().Be(StatusCodes.Status400BadRequest, "$preferred-id should not succeed");
            testContext.Response.Outcome.Issues.Count().Should().Be(1, "An id argument should be provided in the query");
            testContext.Response.Outcome.IssuesWithSeverity(IssueSeverity.Information).Count().Should().Be(1, "The OperationOutcome should be informational, not an error");
        }
    }
}