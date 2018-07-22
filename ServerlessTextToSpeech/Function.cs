using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.SNSEvents;

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;

using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;

using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace ServerlessTextToSpeech
{
    public class Functions
    {
        private DynamoDBContext Context { get; set; }
        /// <summary>
        /// Default constructor that Lambda will invoke.
        /// </summary>
        public Functions()
        {
            Context = new DynamoDBContext(GetClient());
        }

        /// <summary>
        /// Generates a new DynamoDB client.
        /// </summary>
        /// <returns>A DynamoDB client</returns>
        private static AmazonDynamoDBClient GetClient()
        {
            AmazonDynamoDBConfig config = new AmazonDynamoDBConfig();
            AmazonDynamoDBClient client;

            try
            {
                client = new AmazonDynamoDBClient(config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Failed to create a DynamoDB client: {ex.Message}");
                return null;
            }

            return client;
        }

        /// <summary>
        /// Fetches the list of posts from DynamoDB.
        /// </summary>
        /// <param name="request"></param>
        /// <returns>The list of posts</returns>
        public async Task<APIGatewayProxyResponse> GetAllPosts(APIGatewayProxyRequest request, ILambdaContext lambdaContext)
        {
            lambdaContext.Logger.LogLine("Fetching entire DynamoDB table...\n");
            var posts = await Context.ScanAsync<Post>(new List<ScanCondition>()).GetRemainingAsync();

            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(posts)
            };
        }

        /// <summary>
        /// Fetches a single post from DynamoDB.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="lambdaContext"></param>
        /// <returns>A single post</returns>
        public async Task<APIGatewayProxyResponse> GetPostById(APIGatewayProxyRequest request, ILambdaContext lambdaContext)
        {
            string id = request.PathParameters["id"];
            Post post;

            try
            {
                post = await Context.LoadAsync<Post>(id);
            }
            catch (Exception ex)
            {
                lambdaContext.Logger.LogLine($"Could not fetch post #{id}: {ex.Message}");

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.NotFound,
                    Body = "null",
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }

            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(post),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }

        /// <summary>
        /// Creates a new post on DynamoDB with status "PROCESSING" and signals the
        /// ConvertToAudio function to start converting the text.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="lambdaContext"></param>
        /// <returns>The new post's ID</returns>
        public async Task<APIGatewayProxyResponse> CreateNewPost(APIGatewayProxyRequest request, ILambdaContext lambdaContext)
        {
            Post post = JsonConvert.DeserializeObject<Post>(request.Body);

            post.Id = Guid.NewGuid().ToString();
            post.Status = "PROCESSING";

            try
            {
                await Context.SaveAsync<Post>(post);

                var snsClient = new AmazonSimpleNotificationServiceClient();
                var publishRequest = new PublishRequest
                {
                    TopicArn = Environment.GetEnvironmentVariable("SNS_TOPIC"),
                    Message = post.Id
                };

                await snsClient.PublishAsync(publishRequest);
            }
            catch (Exception ex)
            {
                lambdaContext.Logger.LogLine($"Could not save post: {ex.Message}");

                var response = new Dictionary<string, string>
                {
                    { "message", "There was an error saving your post, please try again" }
                };

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Body = JsonConvert.SerializeObject(response),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }

            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.Created,
                Body = JsonConvert.SerializeObject(post),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }

        /// <summary>
        /// Converts a new post's text into speech, uploads audio to S3 and updates post record with S3 URL
        /// </summary>
        /// <param name="snsEvent"></param>
        /// <param name="lambdaContext"></param>
        /// <returns></returns>
        public async Task ConvertToAudio(SNSEvent snsEvent, ILambdaContext lambdaContext)
        {
            string Id = snsEvent.Records[0].Sns.Message;

            lambdaContext.Logger.LogLine($"Text-to-speech function. Post ID in DynamoDB: {Id}");

            var post = await Context.LoadAsync<Post>(Id);

            AudioConverter converter = new AudioConverter();

            post.Url = await converter.ConvertAsync(Id, post.Voice, post.Text);
            post.Status = "UPDATED";
            
            try
            {
                await Context.SaveAsync<Post>(post);
            }
            catch (Exception ex)
            {
                lambdaContext.Logger.LogLine($"Couldn't update post with ID {post.Id}: {ex.Message}");
            }
        }
    }
}
