(*** raw ***)
---
layout: post
title: "RdKafka Subscription in F#"
date: 2016-12-08
comments: false
publish: false
---
(**
## Objective
We want to use the RdKafka library from F#. etc

*)
(**
## Dependencies
1. *RdKafka* from NuGet, or using Paket:

 ```powershell
   ./.paket/paket.exe add nuget RdKafka project MyProject
 ```

2. *Pre-build event* to transfer native libraries (64-bit):

 ```powershell
   xcopy /y /d /f
        "$(ProjectDir)..\packages\RdKafka.Internal.librdkafka\runtimes\win7-x64\native\*.*"
        "$(TargetDir)"
 ```

3. *Reference* and open the C# wrapper:
*)
#r "./packages/RdKafka/lib/net451/RdKafka.dll"

open RdKafka
(*** hide ***)
open System
open System.Collections.Concurrent

(**
## Terminology

### Kafka
*)
type Topic = string
and Partition = int
and Offset = int64
and ErrorReason = string
and BrokerCsv = string  // remove protocol {http://,tcp://}
and ConsumerName = string
(**
### RdKafka Events
*)
type Event =
  | Statistics of string
  | OffsetCommit of ErrorCode*List<Topic*Partition*Offset>
  | EndReached of Topic*Partition*Offset
  | PartitionsAssigned of List<Topic*Partition*Offset>
  | PartitionsRevoked of List<Topic*Partition*Offset>
  | ConsumerError of ErrorCode
  | Error of ErrorCode*string
(**
### Converting RdKafka Types
*)
let ofTopicPartitionOffset (tpo:TopicPartitionOffset) : Topic*Partition*Offset =
  tpo.Topic,tpo.Partition,tpo.Offset
let ofTopicPartitionOffsets =
  Seq.map ofTopicPartitionOffset >> Seq.toList
let ofCommit (oca:Consumer.OffsetCommitArgs) : ErrorCode*List<Topic*Partition*Offset> =
  oca.Error, ofTopicPartitionOffsets oca.Offsets
let ofError (ea:Handle.ErrorArgs) =
  ea.ErrorCode, ea.Reason
(**
### Logging RdKafka Events
RdKafka provides much better visibility than other .NET kafka libraries.
Let the consumer handle all of the cases that can be logged for Event types.
Consider using an exhaustive match on the Event union type.
*)
// 
let toLog = function
  | Event.Error _ -> ()
  | _ -> ()
(**
You can easily attach your `toLog` function to callbacks from both the producer and consumer:
*)
let fromConsumerToLog (consumer:EventConsumer) =
  consumer.OnStatistics.Add(Event.Statistics >> toLog)
  consumer.OnOffsetCommit.Add(ofCommit >> Event.OffsetCommit >> toLog)
  consumer.OnEndReached.Add(ofTopicPartitionOffset >> Event.EndReached >> toLog)
  consumer.OnPartitionsAssigned.Add(ofTopicPartitionOffsets >> Event.PartitionsAssigned >> toLog)
  consumer.OnPartitionsRevoked.Add(ofTopicPartitionOffsets >> Event.PartitionsRevoked >> toLog)
  consumer.OnConsumerError.Add(Event.ConsumerError >> toLog)
  consumer.OnError.Add(ofError >> Event.Error >> toLog)

let fromProducerToLog (source:ConsumerName) (producer:Producer) =
  producer.OnError.Add(ofError >> Event.Error >> toLog)
  producer.OnStatistics.Add(Event.Statistics >> toLog)
(**
## Configuration and Connection
When you connect to RdKafka, you can configure any of the defaults in the underlying native library.
In particular, there are several settings you may want to consider:

1. a consumer _GroupId_, shared by all cooperating instances of a microservice
  * note for rdkafka 0.9.1 or earlier, setting GroupId on a producer may block Dispose()
2. whether or not to _EnableAutoCommit_ for tracking your current offset position
3. whether to save the offsets on the _broker_ for coordination
4. if your Kafka cluster runs an idle connection reaper, disconnection messages will appear at even intervals when idle
5. a _metadata broker list_ enables you to query metadata using the RdKafka C# wrapper
6. where to start a _brand new_ consumer group:
  * `smallest` starts processing from the earliet offset in the topic
  * `largest`, the default, starts from the newest message
*)
let connect (brokerCsv:BrokerCsv) (group:ConsumerName) (autoCommit:bool) =
  let config = new Config()
  config.GroupId <- group                                // (1)
  config.StatisticsInterval <- TimeSpan.FromSeconds(1.0)
  config.EnableAutoCommit <- autoCommit                  // (2)
  config.["offset.store.method"] <- "broker"             // (3)
  config.["log.connection.close"] <- "false"             // (4)
  config.["metadata.broker.list"] <- brokerCsv           // (5)
  config.DefaultTopicConfig <-
    let topicConfig = new TopicConfig()
    topicConfig.["auto.offset.reset"] <- "smallest"      // (6)
    topicConfig

  new EventConsumer(config, brokerCsv),
  new Producer(config, brokerCsv)
(**
A partition key and payload can then be published to a topic.  The response
includes a partition and offset cposition onfirming the write.  Consider
`Encoding.UTF8.GetBytes` if your message is text.
*)
let publish (brokerCsv:BrokerCsv) (group:ConsumerName) (topic:Topic) =
  let consumer,producer = connect brokerCsv group false
  let topic = producer.Topic(topic)
  fun (key:byte[], payload:byte[]) -> async {
    let! report = Async.AwaitTask(topic.Produce(payload=payload,key=key))
    return report.Partition, report.Offset
  }
(**
To consume, on partition assignment we select `Offset.Stored` - (6) that now defaults
to `smallest` if no stored offset exists.  Messages are then sent to the onMessage callback
once the topic subscription is started.
*)
let subscribeCallback (brokerCsv:BrokerCsv) (group:ConsumerName) (topic:Topic) (onMessage) =
  let autoCommit = true
  let consumer,producer = connect brokerCsv group autoCommit
  consumer.OnPartitionsAssigned.Add(
    ofTopicPartitionOffsets
    >> List.map (fun (t,p,o) -> t,p,Offset.Stored)
    >> List.map TopicPartitionOffset
    >> Collections.Generic.List<_>
    >> consumer.Assign)
  consumer.OnMessage.Add(onMessage)
  consumer.Subscribe(new Collections.Generic.List<string>([topic]))
  consumer.Start()
(**
The above works quite well _assuming you process the message to completion within the callback_.

If you want to process larger batches of messages using a sequence instead, a blocking
collection can used to buffer incoming messages as they're received.  A sequence generator
is returned allowing the consumer to iterate or batch the topic's messages.
*)
let subscribeSeq (brokerCsv:BrokerCsv) (group:ConsumerName) (topic:Topic) =
  let autoCommit = true
  let consumer,producer = connect brokerCsv group autoCommit
  consumer.OnPartitionsAssigned.Add(
    ofTopicPartitionOffsets
    >> List.map (fun (t,p,o) -> t,p,Offset.Stored)
    >> List.map TopicPartitionOffset
    >> Collections.Generic.List<_>
    >> consumer.Assign)

  let buffer = 3000
  let messages =
    new Collections.Concurrent.BlockingCollection<Message>(
      new Collections.Concurrent.ConcurrentQueue<Message>(), buffer)
  consumer.OnMessage.Add(messages.Add) // > 3000 messages in the buffer will hold/block the callback
  consumer.Subscribe(new Collections.Generic.List<string>([topic]))
  consumer.Start()
  
  Seq.initInfinite(fun _ ->
    let message = messages.Take()
    message.Partition, message.Offset, message.Payload)  // or AsyncSeq :)
(**
At this point, we make an important observation: the client may commit offsets
acknowledging that a message in the buffer has been processed _even though this
may not have been dequeued_ yet.  From RdKafka's point of view it's complete,
however.

If you process messages sequentially on a single thread, using a buffer size of 1
is an adequate workaround.  However, if you're processing messages in larger groups
or with multiple threads (using `AsyncSeq.iterAsyncParallel`, for instance), you'll
want to manage offsets yourself.

## Manual Offsets

*)
type Offsets =
  { Next : Offset option       // the next offset to be committed
    Active : Offset list       // offsets of active messages (started processing)
    Processed : Offset list }  // offsets of processed messages newer than (>) any active
                               // message (e.g., still working on 4L, but 5L & 6L are processed)
    
// start a message, finish a message, update "next" offset to be committed
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Offsets =
  let private update = function
    | { Processed=[] } as x -> x
    | { Active=[] } as x -> { x with Processed=[]; Next=List.max x.Processed |> Some }
    | x -> x.Processed
           |> List.partition ((>=) (List.min x.Active))
           |> function | [], _ -> x
                       | c, p -> { x with Processed=p; Next=List.max c |> Some }
  let start  (x:Offset) (xs:Offsets) = update { xs with Active = x :: xs.Active }
  let finish (x:Offset) (xs:Offsets) = update { xs with Active = List.filter ((<>) x) xs.Active
                                                        Processed = x :: xs.Processed }

              
    
type Watermark =               // watermark high/low offset for each partition of a topic
  { Topic : string             // topic
    Partition : int            // partition of the topic
    High : int64               // the highest offset available in that partition
    Low : int64 }              // the lowest offset available in that partition

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Watermark =
  let queryWithTimeout (timeout) (producer:Producer) (consumer:Consumer) (topic:Topic) =
    let queryWatermark(t, p) =
      async {
        let! w = Async.AwaitTask <| consumer.QueryWatermarkOffsets(TopicPartition(t, p), timeout)
        return { Topic=t; Partition=p; High=w.High; Low=w.Low }
      }
    async {
      let topic = producer.Topic(topic)
      let! metadata = Async.AwaitTask <| producer.Metadata(onlyForTopic=topic, timeout=timeout)
      return!
        metadata.Topics
        |> Seq.collect(fun t -> t.Partitions |> Seq.map (fun p -> t.Topic, p.PartitionId))
        |> Seq.sort
        |> Seq.map queryWatermark
        |> Async.Parallel // use open source AsyncSeq, instead :)
    }
                     
type Checkpoint = List<Partition*Offset>

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Checkpoint =
  let queryWithTimeout (timeout) (producer:Producer) (consumer:Consumer) (topic:Topic) : Async<Checkpoint> =
    async {
      let! metadata = 
        producer.Metadata(onlyForTopic=producer.Topic(topic), timeout=timeout)
        |> Async.AwaitTask
      let partitions =
        metadata.Topics
        |> Seq.collect(fun t -> t.Partitions |> Seq.map (fun p -> t.Topic, p.PartitionId))
        |> Seq.sort
        |> Seq.map (fun (t,p) -> new TopicPartition(t,p))
      let! committed =
        consumer.Committed(new Collections.Generic.List<_>(partitions), timeout)
        |> Async.AwaitTask
      return
        committed
        |> Seq.map (fun tpo -> tpo.Partition, tpo.Offset)
        |> Seq.sortBy fst
        |> Seq.toList
    }

module OffsetMonitor =
  let assign _ = ()
  let revoke _ = ()
    
let subscribe (brokerCsv:BrokerCsv) (group:ConsumerName) (topic:Topic) =
  let autoCommit = false
  let consumer,producer = connect brokerCsv group autoCommit
  consumer.OnPartitionsAssigned.Add(
    ofTopicPartitionOffsets
    >> List.map (fun (t,p,o) -> t,p,Offset.Stored)
    >> List.map TopicPartitionOffset
    >> Collections.Generic.List<_>
    >> consumer.Assign)

  consumer.OnPartitionsAssigned.Add(ofTopicPartitionOffsets >> OffsetMonitor.assign)
  consumer.OnPartitionsRevoked.Add(ofTopicPartitionOffsets >> OffsetMonitor.revoke)

  let messageSeq =
    let buffer = 3000
    let messages =
      new Collections.Concurrent.BlockingCollection<Message>(
        new Collections.Concurrent.ConcurrentQueue<Message>(), buffer)
    consumer.OnMessage.Add(messages.Add)
    Seq.initInfinite(fun _ -> messages.Take())
  consumer.Subscribe(new Collections.Generic.List<string>([topic]))
  consumer.Start()
  
  ()

  