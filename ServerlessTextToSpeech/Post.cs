using System;
using System.Collections.Generic;
using System.Text;

using Amazon.DynamoDBv2.DataModel;

namespace ServerlessTextToSpeech
{
    [DynamoDBTable("PostNetCore")]
    class Post
    {
        [DynamoDBHashKey]
        public string Id { get; set; }

        [DynamoDBProperty]
        public string Status { get; set; }

        [DynamoDBProperty]
        public string Text { get; set; }

        [DynamoDBProperty]
        public string Voice { get; set; }
    }
}
