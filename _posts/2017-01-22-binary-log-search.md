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

log as in linear data store (kafka, eventstore)
log as in logarithmic complexity (assuming the monotonic search field exists)
binary search algorithm
jsonpath query (automata) to extract monotonically increasing field (i.e., date, incremental id)

https://github.com/j-alexander/binary-log-search

integration of c# UI with f# search + data i/o

nata api for eventstore (official client) + kafka (jroland client)

### Goal


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



### Design & Implementation