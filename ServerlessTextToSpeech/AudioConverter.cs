using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Amazon;
using Amazon.Polly;
using Amazon.Polly.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace ServerlessTextToSpeech
{
    class AudioConverter
    {
        /// <summary>
        /// Converts text to speech with the given voice and uploads audio file to S3.
        /// </summary>
        /// <param name="id">Post ID in DynamoDB</param>
        /// <param name="voiceId">Voice to be used in speech</param>
        /// <param name="text">Text to be converted</param>
        /// <returns>The file's URL in S3</returns>
        public async Task<string> ConvertAsync(string id, string voiceId, string text)
        {
            var pollyClient = new AmazonPollyClient();

            IEnumerable<string> textBlocks = TruncateText(text);
            
            foreach (string block in textBlocks)
            {
                var request = new SynthesizeSpeechRequest
                {
                    OutputFormat = OutputFormat.Mp3,
                    Text = block,
                    VoiceId = voiceId
                };

                try
                {
                    var response = await pollyClient.SynthesizeSpeechAsync(request);

                    WriteToFile(response.AudioStream, id);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            return await UploadAsync(id);
        }

        /// <summary>
        /// Truncate text in approximately 1000-character blocks
        /// to pass to Polly SynthesizeSpeech, since it can only
        /// transform text with about 1500 characters per call.
        /// </summary>
        /// <param name="text">The text to truncate</param>
        /// <returns>List of text blocks</returns>
        private IEnumerable<string> TruncateText(string text)
        {
            string rest = text;

            while (rest.Length > 1100)
            {
                int end = rest.Substring(0, 1000).IndexOf('.');

                if (end < 0)
                {
                    end = rest.Substring(0, 1000).IndexOf(' ');
                }

                string textBlock = rest.Substring(0, end + 1);
                rest = rest.Substring(end + 1).Trim();

                yield return textBlock;
            }

            yield return rest;
        }

        /// <summary>
        /// Appends Polly's AudioStream to a Lambda temp file.
        /// </summary>
        /// <param name="stream">AudioStream returned from Polly SynthesizeSpeech call</param>
        /// <param name="id">DynamoDB record ID</param>
        private void WriteToFile(Stream stream, string id)
        {
            string path = $"/tmp/{id}.mp3";
            using (FileStream fs = new FileStream(path, FileMode.Append, FileAccess.Write))
            {
                stream.CopyTo(fs);
            }
        }

        /// <summary>
        /// Upload the audio file to S3.
        /// </summary>
        /// <param name="id">The post ID in DynamoDB</param>
        /// <returns>The file's S3 URL</returns>
        private async Task<string> UploadAsync(string id)
        {
            AmazonS3Client client = new AmazonS3Client();

            TransferUtility tu = new TransferUtility(client);

            string bucketName = Environment.GetEnvironmentVariable("BUCKET_NAME");

            var uploadRequest = new TransferUtilityUploadRequest
            {
                BucketName = bucketName,
                FilePath = $"/tmp/{id}.mp3",
                StorageClass = S3StorageClass.Standard,
                Key = $"{id}.mp3",
                CannedACL = S3CannedACL.PublicRead
            };

            await tu.UploadAsync(uploadRequest);

            string cdnDistribution = Environment.GetEnvironmentVariable("CLOUDFRONT_DISTRIBUTION");
            string url = $"{cdnDistribution}/{id}.mp3";

            return url;
        }

        /// <summary>
        /// Get the S3 bucket's region.
        /// </summary>
        /// <param name="client"></param>
        /// <returns>The S3Region object representing the bucket region</returns>
        private async Task<S3Region> GetBucketLocationAsync(IAmazonS3 client)
        {
            GetBucketLocationRequest request = new GetBucketLocationRequest
            {
                BucketName = Environment.GetEnvironmentVariable("BUCKET_NAME")
            };

            GetBucketLocationResponse response = await client.GetBucketLocationAsync(request);

            return response.Location;
        }
    }
}
