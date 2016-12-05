﻿(*** raw ***)
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
1. *RdKafka* from NuGet

 Using Paket:
 `.\.paket\paket.exe add nuget RdKafka project MyProject`

2. *Pre-build event* for native libraries:

 ```
    xcopy /y /d /f
        "$(ProjectDir)..\packages\RdKafka.Internal.librdkafka\runtimes\win7-x64\native\*.*"
        "$(TargetDir)"
 ```

3. *Reference* and open the C# wrapper:
*)
#r "../packages/RdKafka/lib/net451/RdKafka.dll"

open RdKafka
open System
(**
## Terminology
*)
type Topic = string
and Partition = int
and Offset = int64
and ErrorReason = string
and BrokerCsv = string  // remove protocol {http://,tcp://}
and ConsumerName = string

type Event =
  | Statistics of string
  | OffsetCommit of ErrorCode*List<Topic*Partition*Offset>
  | EndReached of Topic*Partition*Offset
  | PartitionsAssigned of List<Topic*Partition*Offset>
  | PartitionsRevoked of List<Topic*Partition*Offset>
  | ConsumerError of ErrorCode
  | Error of ErrorCode*string

let ofTopicPartitionOffset (tpo:TopicPartitionOffset) : Topic*Partition*Offset =
  tpo.Topic,tpo.Partition,tpo.Offset
let ofTopicPartitionOffsets =
  Seq.map ofTopicPartitionOffset >> Seq.toList
let ofCommit (oca:Consumer.OffsetCommitArgs) : ErrorCode*List<Topic*Partition*Offset> =
  oca.Error, ofTopicPartitionOffsets oca.Offsets
let ofError (ea:Handle.ErrorArgs) =
  ea.ErrorCode, ea.Reason

// let the consumer handle all of the cases that can be logged in Event
let toLog = function
  | Event.Error _ -> ()
  | _ -> ()
    
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

let connect (brokerCsv:BrokerCsv) (group:ConsumerName) (autoCommit:bool) =
  let config = new Config()
  config.GroupId <- group
  config.StatisticsInterval <- TimeSpan.FromSeconds(1.0)
  config.EnableAutoCommit <- autoCommit // or commit offsets ourselves?
  config.["offset.store.method"] <- "broker" // save offsets on broker
  config.["log.connection.close"] <- "false" // reaper causes close events
  config.["metadata.broker.list"] <- brokerCsv // query metadata (fix for null in wrapper)
  config.DefaultTopicConfig <-
    let topicConfig = new TopicConfig()
    topicConfig.["auto.offset.reset"] <- "smallest" // if new group, start at oldest msg? by default starts at newest
    topicConfig

  new EventConsumer(config, brokerCsv),
  new Producer(config, brokerCsv)


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
  consumer.OnMessage.Add(messages.Add)
  consumer.Subscribe(new Collections.Generic.List<string>([topic]))
  consumer.Start()
  
  Seq.initInfinite(fun _ ->
    let message = messages.Take()
    message.Partition, message.Offset, message.Payload)  // or asyncseq :)


let publish (brokerCsv:BrokerCsv) (group:ConsumerName) (topic:Topic) =
  let consumer,producer = connect brokerCsv group false
  let topic = producer.Topic(topic)
  fun (key:byte[], payload:byte[]) -> async {
    let! report = Async.AwaitTask(topic.Produce(payload=payload,key=key))
    return report.Partition, report.Offset
  }


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

  