---
layout: post
title: "Binary Log Search"
date: 2017-01-22
comments: false
publish: false
---
### Problem
messages are in linear data store (kafka, eventstore) stored as json
messages contains monotonically increasing field (timestamp, incremental id, etc)
need to be able to quickly find the precise offset of a specific value for that field

### Background
So what does it mean to 
log as in linear data store (kafka, eventstore)
log as in logarithmic complexity (assuming the monotonic search field exists)
binary search algorithm
jsonpath query (automata) to extract monotonically increasing field (i.e., date, incremental id)

https://github.com/j-alexander/binary-log-search

integration of c# UI with f# search + data i/o

## Design

Need to be able to use either sequences from EventStore or Kafka.

 - Must be able to query first, last, and specific offsets.
 - Use Nata.IO library for abstraction over:
   - EventStore (Official Client)
   - Kafka (JRoland Client)


Need to be able to report progress to WPF UI.

Need to be able to query DateTime from arbitrary Json document:

 - Use [JsonPath for FSharp.Data.JsonValue](http://localhost:4000/entry/2016/12/23/jsonpath-queries-using-fsharpdata)
   - (need to publish it)

### Algorithm

Binary Search algorithm with both seek and scanning capability.

 - It's faster to scan every message in a range than to query/seek specific offsets with a pure binary search.
   - In practice, this threshold is around ~1000 messages.

 - Also, a JsonPath query may not return a value for some document.
   - It's easy to check the next one in sequence (up to the upper limit).

### Implementation

 - Link to sections of the code:

### Integration Challenges (C\#/F\#)

 - Examples of specific types

### Result

shows dropdown for both es & kafka connection strings
selects stream or topic name
provides target date and jsonpath query expression to _possibly_ extract dates from a document (does not need to match all documents!)
option to skip fixed-width pre-amble (needed for my use case)

when query executes:
shows Name of topic+partition (or stream)
Range - width of interval to check [From..To]
Query+Current position selected by Binary Search
Status
 - Seek (jumps from message to message by offset)
 - Scan (reads all events sequentially)
 - Cancelled
 - Found

![application screenshot](Screenshot.gif)