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
RdKafka provides much better transparency than other .NET kafka libraries.
Let the consumer handle all of the cases that can be logged for Event types.
Consider using an exhaustive pattern match on the Event union type.
*)
let toLog = function
  | Event.Error _ -> () 
//| ...
(*** hide ***)
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
  * note: for rdkafka 0.9.1 or earlier, setting GroupId on a producer may block Dispose()
2. whether or not to _EnableAutoCommit_ for tracking your current offset position
3. whether to save the offsets on the _broker_ for coordination
4. if your Kafka cluster runs an idle connection reaper, disconnection messages will appear at even intervals when idle
5. a _metadata broker list_ workaround enables you to query additional metadata using the native wrapper
6. where to start a _brand new_ consumer group:
  * `smallest` starts processing from the earliet offsets in the topic
  * `largest`, the default, starts from the newest message offsets
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
### Publishing
A partition key and payload can then be published to a topic.  The response
includes a partition and offset position confirming the write.  Consider
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
### Subscribing
To consume, on partition assignment we select `Offset.Stored`, which defaults to the value of
`auto.offset.reset`, if no stored offset exists.  Messages are then sent to the onMessage callback
once the topic subscription starts.
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

You may want to process larger batches of messages asynchronously, however.
To use a sequence instead, a blocking collection can buffer incoming messages as they're received.
A sequence generator yielding messages from this buffer is returned to the client:
*)
let subscribeSeq brokerCsv group topic : seq<Partition*Offset*byte[]> =
(*** hide ***)
  let autoCommit = true
  let consumer,producer = connect brokerCsv group autoCommit
  consumer.OnPartitionsAssigned.Add(
    ofTopicPartitionOffsets
    >> List.map (fun (t,p,o) -> t,p,Offset.Stored)
    >> List.map TopicPartitionOffset
    >> Collections.Generic.List<_>
    >> consumer.Assign)
(**
More than _3000_ messages in the buffer will hold+block the callback until the client has
consumed from the sequence.
*)
  let buffer = 3000
  let messages =
    new Collections.Concurrent.BlockingCollection<Message>(
      new Collections.Concurrent.ConcurrentQueue<Message>(), buffer)
  consumer.OnMessage.Add(messages.Add) 
  consumer.Subscribe(new Collections.Generic.List<string>([topic]))
  consumer.Start()
  
  Seq.initInfinite(fun _ ->
    let message = messages.Take()
    message.Partition, message.Offset, message.Payload)  // or AsyncSeq :)
(**
At this point, we make an important observation: the client may commit offsets
acknowledging that a message in the buffer has been processed _even though it
may not have been dequeued_.  From RdKafka's point of view, the callback for
that message has completed!

This is when you'll want to manage offsets yourself.

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
(**
## Supervision

Supervising progress of a microservice running RdKafka depends on monitoring two things:
1. the range of messages available in topic (i.e. its `Watermark` offsets), and
2. the current `Checkpoint` offsets for a consumer group relative to those `Watermarks`

### Watermarks

A line drawn on the side of an empty ship is its low water line.  When you put cargo on the
ship, it sits lower in water.  The line drawn on the side of a fully loaded ship is its
high water line.  The same terminology is used here:
*)
type Watermark =     // watermark high/low offset for each partition of a topic
  { Topic : string   // topic
    Partition : int  // partition of the topic
    High : int64     // highest offset available in this partition (newest message)
    Low : int64 }    // lowest offset available in this partition (oldest message)
(**
To query these watermarks:
*)
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
        |> Async.Parallel // again, consider AsyncSeq instead :)
    }
(**
### Checkpoints
Your consumer group's current position is composed of an offset position within each partition
of the topic:
*) 
type Checkpoint = List<Partition*Offset>
(**
If you're monitoring within an active consumer, you have access to the absolute latest offsets
completed for each partition.  When multiple consumers are working together in a group, however,
each has only a partial view of the overall progress.

It's possible to query the broker for the latest committed checkpoint for a consumer group
across all partitions of the topic:
*)
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
(**
Over time, your current offset in each partition should increase as your consumer group
processes messages.  Similarly, the high watermark will also increase as new messages are
added.  The difference between your high watermark and your current position is your lag.

Using these figures, you can measure your performance relative to any service level agreement
in effect for your microservice, and potentially take corrective action - such as scaling
the number of consumer instances or size of machines.
*)