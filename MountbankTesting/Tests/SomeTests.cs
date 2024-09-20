using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MbDotNet;
using MbDotNet.Models;
using MbDotNet.Models.Predicates;
using MbDotNet.Models.Predicates.Fields;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Tests
{
    [TestClass]
    public class SomeTests
    {
        // Set up Mountebank to run in a container.
        private static readonly IContainer Container = new ContainerBuilder()
            .WithImage("bbyars/mountebank")
            .WithPortBinding(2525, true)
            .WithPortBinding(8000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(2525))
            .WithHostname("localhost")
            .WithEntrypoint("mb", "--debug")
            .Build();

        private static MountebankClient _mountebankClient;
        private readonly HttpClient _httpClient = new();


        [ClassInitialize]
        public static async Task StartMountebank(TestContext context)
        {
            // Start Mountebank container.
            await Container.StartAsync();

            _mountebankClient = new(new Uri($"http://{Container.Hostname}:{Container.GetMappedPublicPort(2525)}"));
        }

        [ClassCleanup]
        public static async Task StopMountebank()
        {
            // Stop Mountebank container.
            await Container.StopAsync();
        }

        [TestCleanup]
        public async Task ResetMountebank()
        {
            // Wipe everything that has been configured in Mountebank after each test.
            await _mountebankClient.DeleteAllImpostersAsync();
        }



        [TestMethod]
        public async Task SimpleNotFound()
        {
            var imposterPort = Container.GetMappedPublicPort(8000);

            // Simple stub that always returns 404.
            await _mountebankClient.CreateHttpImposterAsync(8000, imposter =>
            {
                imposter.AddStub().ReturnsStatus(HttpStatusCode.NotFound);
            });

            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{imposterPort}/customers?id=123");

            var response = await _httpClient.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [TestMethod]
        public async Task GetJsonResponse()
        {
            var imposterPort = Container.GetMappedPublicPort(8000);

            // Stub that returns a JSON serialized JobStatus for the specified GET request.
            await _mountebankClient.CreateHttpImposterAsync(8000, "Job Service", imposter =>
            {
                imposter.AddStub()
                    .OnPathAndMethodEqual("/job/job1", Method.Get)
                    .ReturnsJson(HttpStatusCode.OK, new JobStatus { Id = "job1", Errors = 2, Warnings = 1, Deferred = false });
            });

            var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{imposterPort}/job/job1");
            var response = await _httpClient.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var body = await response.Content.ReadAsStringAsync();

            var jobStatus = JsonConvert.DeserializeObject<JobStatus>(body);
            jobStatus.Should().NotBeNull();
            jobStatus!.Id.Should().Be("job1");
            jobStatus.Errors.Should().Be(2);
            jobStatus.Warnings.Should().Be(1);
            jobStatus.Deferred.Should().BeFalse();
        }

        [TestMethod]
        public async Task SimplePredicateMatching()
        {
            var imposterPort = Container.GetMappedPublicPort(8000);

            // Returns specified body only when the request body matches.
            await _mountebankClient.CreateHttpImposterAsync(8000, "Echo Service", imposter =>
            {
                imposter.AddStub()
                    .OnPathAndMethodEqual("/echo", Method.Post)
                    .On(new EqualsPredicate<HttpPredicateFields>(new HttpPredicateFields
                    {
                        RequestBody = "Bob"
                    }))
                    .ReturnsBody(HttpStatusCode.OK, "Hello, Bob!");
            });

            var response = await _httpClient.PostAsync($"http://localhost:{imposterPort}/echo", new StringContent("bob"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("Hello, Bob!");
        }

        [TestMethod]
        public async Task LoosePredicateMatching()
        {
            var imposterPort = Container.GetMappedPublicPort(8000);

            // Returns specified body only when the request body matches.
            await _mountebankClient.CreateHttpImposterAsync(8000, "Echo Service", imposter =>
            {
                imposter.AddStub()
                    .OnPathAndMethodEqual("/echo", Method.Post)
                    .On(new ContainsPredicate<HttpPredicateFields>(new HttpPredicateFields
                    {
                        RequestBody = "Bob"
                    }))
                    .ReturnsBody(HttpStatusCode.OK, "Hello, Bob!");
            });

            var response = await _httpClient.PostAsync($"http://localhost:{imposterPort}/echo", new StringContent("Hi, this is Bob!"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("Hello, Bob!");
        }

        [TestMethod]
        public async Task MultipleResponses()
        {
            var imposterPort = Container.GetMappedPublicPort(8000);

            await _mountebankClient.CreateHttpImposterAsync(8000, "Echo Service", imposter =>
            {
                // Stub that returns a rotating response each time it is called.
                imposter.AddStub()
                    .OnPathAndMethodEqual("/echo", Method.Post)
                    .On(new EqualsPredicate<HttpPredicateFields>(new HttpPredicateFields
                    {
                        RequestBody = "Bob"
                    }))
                    .ReturnsBody(HttpStatusCode.OK, "Hello, Bob!")
                    .ReturnsBody(HttpStatusCode.OK, "Greetings, Bob!");
            });

            var response = await _httpClient.PostAsync($"http://localhost:{imposterPort}/echo", new StringContent("Bob"));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("Hello, Bob!");

            var response2 = await _httpClient.PostAsync($"http://localhost:{imposterPort}/echo", new StringContent("Bob"));
            response2.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response2.Content.ReadAsStringAsync()).Should().Be("Greetings, Bob!");

            var response3 = await _httpClient.PostAsync($"http://localhost:{imposterPort}/echo", new StringContent("Bob"));
            response3.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response3.Content.ReadAsStringAsync()).Should().Be("Hello, Bob!");
        }

        [TestMethod]
        public async Task MultipleStubsWithDefault()
        {
            var imposterPort = Container.GetMappedPublicPort(8000);

            await _mountebankClient.CreateHttpImposterAsync(8000, "Echo Service", imposter =>
            {
                // Stubs to return unique responses for different requests, with a default response.
                imposter.AddStub()
                    .OnPathAndMethodEqual("/echo", Method.Post)
                    .On(new EqualsPredicate<HttpPredicateFields>(new HttpPredicateFields
                    {
                        RequestBody = "Alice"
                    }))
                    .ReturnsBody(HttpStatusCode.OK, "Hello, Alice!");

                imposter.AddStub()
                    .OnPathAndMethodEqual("/echo", Method.Post)
                    .On(new EqualsPredicate<HttpPredicateFields>(new HttpPredicateFields
                    {
                        RequestBody = "Bob"
                    }))
                    .ReturnsBody(HttpStatusCode.OK, "Hello, Bob!");

                imposter.AddStub()
                    .ReturnsBody(HttpStatusCode.OK, "I don't know you!");
            });

            var response = await _httpClient.PostAsync($"http://localhost:{imposterPort}/echo", new StringContent("Alice"));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("Hello, Alice!");

            var response2 = await _httpClient.PostAsync($"http://localhost:{imposterPort}/echo", new StringContent("Bob"));
            response2.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response2.Content.ReadAsStringAsync()).Should().Be("Hello, Bob!");

            var response3 = await _httpClient.PostAsync($"http://localhost:{imposterPort}/echo", new StringContent("Charlie"));
            response3.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response3.Content.ReadAsStringAsync()).Should().Be("I don't know you!");
        }

        [TestMethod]
        public async Task RequireBearerTokenInAuthorizationHeader()
        {
            var imposterPort = Container.GetMappedPublicPort(8000);

            // Returns specified body only when the request body matches.
            var imposter = await _mountebankClient.CreateHttpImposterAsync(8000, "Echo Service", imposter =>
            {
                //imposter.RecordRequests = true;
                imposter.AddStub()
                    .OnPathAndMethodEqual("/echo", Method.Post)
                    .On(new EqualsPredicate<HttpPredicateFields>(new HttpPredicateFields
                    {
                        Headers = new Dictionary<string, object>
                        {
                            { "Authorization", "Bearer good" }
                        },
                        RequestBody = "Bob"
                    }))
                    .ReturnsBody(HttpStatusCode.OK, "Hello, Bob!");
                imposter.AddStub()
                    .OnPathAndMethodEqual("/echo", Method.Post)
                    .On(new EqualsPredicate<HttpPredicateFields>(new HttpPredicateFields
                    {
                        Headers = new Dictionary<string, object>
                        {
                            { "Authorization", "Bearer bad" }
                        },
                        RequestBody = "Bob"
                    }))
                    .ReturnsBody(HttpStatusCode.Unauthorized, "Go away!");
            });

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "good");
            var response = await _httpClient.PostAsync($"http://localhost:{imposterPort}/echo", new StringContent("bob"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("Hello, Bob!");

            var configuredImposter = await _mountebankClient.GetHttpImposterAsync(8000);
            configuredImposter.NumberOfRequests.Should().Be(1);
            configuredImposter.Stubs[0].Matches.Count.Should().Be(1);
            configuredImposter.Stubs[1].Matches.Count.Should().Be(0);
        }

        [TestMethod]
        public async Task FormBodyPredicateMatching()
        {
            var imposterPort = Container.GetMappedPublicPort(8000);

            // Returns specified body only when the request body matches.
            await _mountebankClient.CreateHttpImposterAsync(8000, "Foo Service", imposter =>
            {
                imposter.AddStub()
                    .OnPathAndMethodEqual("/api/foo", Method.Post)
                    .On(new EqualsPredicate<HttpPredicateFields>(new HttpPredicateFields
                    {
                        FormContent = new Dictionary<string, string>
                        {
                            { "x", "one" },
                            { "y", "two" },
                            { "z", "three" }
                        }
                    }))
                    .ReturnsBody(HttpStatusCode.OK, "Hello");
            });

            var response = await _httpClient.PostAsync($"http://localhost:{imposterPort}/api/foo", new FormUrlEncodedContent(
                new KeyValuePair<string, string>[]
                {
                    new("x", "one"),
                    new("y", "two"),
                    new("z", "three")
                }
            ));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("Hello");
        }

        [TestMethod]
        public async Task RecordBinaryPostedBody()
        {
            var imposterPort = Container.GetMappedPublicPort(8000);

            await _mountebankClient.CreateHttpImposterAsync(8000, "Binary Post Service", imposter =>
            {
                imposter.AddStub()
                    .OnPathAndMethodEqual("/binary", Method.Post)
                    .ReturnsStatus(HttpStatusCode.OK);
                imposter.RecordRequests = true;
            });

            var response = await _httpClient.PostAsync($"http://localhost:{imposterPort}/binary", new ByteArrayContent(new byte[] { 0x01, 0x02, 0x03, (byte)'a', (byte)'b' }));
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var imposter = await _mountebankClient.GetHttpImposterAsync(8000);

            var postedBody = imposter.Requests.First().Body;

            postedBody.Should().NotBeNull();

            var originalBytes = Encoding.UTF8.GetBytes(postedBody);

            originalBytes.Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03, (byte)'a', (byte)'b' });
        }
    }
}