---
layout: post
title: "RdKafka Subscription in F#"
date: 2016-12-05
comments: false
publish: false
---
* Playing with code highlighting.

```ocaml
(*
Add nuget package "RdKafka", e.g. using Paket:
.\.paket\paket.exe add nuget RdKafka project MyProject

Pre-build event for native libraries:
xcopy /y /d /f "$(ProjectDir)..\packages\RdKafka.Internal.librdkafka\runtimes\win7-x64\native\*.*" "$(TargetDir)"
*)

// and the wrapper:
#r "../packages/RdKafka/lib/net451/RdKafka.dll"

open RdKafka
open System

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
```


```fsharp
(*
Add nuget package "RdKafka", e.g. using Paket:
.\.paket\paket.exe add nuget RdKafka project MyProject

Pre-build event for native libraries:
xcopy /y /d /f "$(ProjectDir)..\packages\RdKafka.Internal.librdkafka\runtimes\win7-x64\native\*.*" "$(TargetDir)"
*)

// and the wrapper:
#r "../packages/RdKafka/lib/net451/RdKafka.dll"

open RdKafka
open System

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
```


```sml
(*
Add nuget package "RdKafka", e.g. using Paket:
.\.paket\paket.exe add nuget RdKafka project MyProject

Pre-build event for native libraries:
xcopy /y /d /f "$(ProjectDir)..\packages\RdKafka.Internal.librdkafka\runtimes\win7-x64\native\*.*" "$(TargetDir)"
*)

// and the wrapper:
#r "../packages/RdKafka/lib/net451/RdKafka.dll"

open RdKafka
open System

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
```