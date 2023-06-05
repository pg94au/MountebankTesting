using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MbDotNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    [TestClass]
    public class SomeTests
    {
        private readonly IContainer _container = new ContainerBuilder()
            .WithImage("bbyars/mountebank")
            .WithName("mountebank")
            .WithPortBinding(2525, 2525)
            .WithExposedPort(3000)
            .WithExposedPort(3100)
            .WithPortBinding(8000, 8000)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(2525))
            .WithHostname("localhost")
            .Build();

        private readonly MountebankClient _mountebankClient = new();
        private readonly HttpClient _httpClient = new HttpClient();


        [TestInitialize]
        public async Task Initialize()
        {
            await _container.StartAsync();
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            await _container.StopAsync();
        }

        [TestMethod]
        public async Task Test()
        {
            await _mountebankClient.CreateHttpImposterAsync(8000, imposter =>
            {
                imposter.AddStub().ReturnsStatus(HttpStatusCode.NotFound);
            });

            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost:8000/customers?id=123");

            var response = await _httpClient.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}