---
layout: post
title: "Binary Log Search"
date: 2017-02-22
comments: true
publish: true
---

Following is a case study on a fun evening project in [F\#](http://fsharp.org/) for [Kafka](https://kafka.apache.org/) and [EventStore](https://geteventstore.com/):

## Problem

The product matching and taxonomy engine at [Jet.com](http://tech.jet.com/) is a large event driven distributed system consisting of nearly 400 microservices
that process a half million messages per minute on a quiet Sunday.  Clustered linear data stores, such as Kafka and EventStore, are used
to retain and distribute many of these messages.

For auditing and diagnostic purposes, finding an individual message in these datastores could entail scanning a huge number of 
events in linear time, algorithmically on the order of `O(n)`.  In practice, this could take days.

Although recent versions of these technologies maintain both timestamps and sequence numbers, we may want to search based on the
value of monotonically increasing fields _inside_ the event's Json document.  Any field containing a date, incremental id
or value other than the message commit date and sequence number.  This is often possible because of ordering guarantees among
upstream microservices.

The goal is to locate the precise offset (and partition in the case of Kafka) of messages at the lower bound of the desired
date time, or id.  And this needs to be done quickly without drawing down and processing the vast number of messages in the
data store.

### Requirements

Any solution should support technical PMs who need to quickly locate such a message.  There may be an urgent need to trace down
the contents of an event that could have been transmitted during a large window two or three days ago.  It needs to be able to accept
different path specifiers to identify the search field for different kinds of documents, and be able to query arbitrary topics and
streams.  It's possible that not all messages in a stream even contain the desired search field, so it must operate within an environment
of partial information.

Thus, the solution should be be friendly, fast, _and_ flexible.

## Strategy

We assume that the contents of the target search field is monotonically increasing.  Imagine the following sample from
a stream of data:

| Partition | Offset | Contents |
| -: | -: | :- |
| `7` | `31415` | `{ "target": 296962622 }` |
| `7` | `31416` | `{ "target": 296963894 }` |
| `7` | `31417` | `{ "target": 296963894 }` |
| `7` | `31418` | `{ "target": 296967168 }` |
| `7` | `31419` | `{ "decoy": 1024513301 }` |
| `7` | `31420` | `{ "target": 296970554 }` |
| `7` | `31421` | `{ "target": 296971532 }` |

Note that the `target` field value is trending upward, but not necessarily one-to-one with the offset value.  It may
be missing `[31419]`, and it may appear several times `[31416;31417]`.

### Binary Search Algorithm

Fortunately, this problem looks like a slight variation of the childhood [number guessing game](https://www.funbrain.com/cgi-bin/gn.cgi?A1=s&A2=100&A3=1).
In that game, you ask a child to guess _what number you're thinking of_ between `1` and `100`.  When they say "4", you respond,
"Nope, it's bigger than 4!"  An astute child will then guess some number between `5` and `100` to earn a cookie.

In introductory computer science, the [binary search algorithm](https://en.wikipedia.org/wiki/Binary_search_algorithm) solves that game in logarithmic
time, or `O(log n)`.

If you're looking for a message with `"target": 296971532`, you might start by probing offset `31418`.  Seeing that `296967168` is too low, you might
then look at offset `31420` and then `31421` to find the lower bound of `296971532`.

#### Partial Information

However, if you probe partition `7`, offset `31419` above for the `target` field, you will be unable to extract the `target` value.
Thankfully, most clients for Kafka and EventStore are designed to efficiently stream subsequent events.
In this case, the algorithm needs to be modified to also consider `target` values later in the stream including offsets `31420` and above.
It should also be able to handle situations where the tail of the event stream _entirely excludes_ this `target` field.

#### Optimization

To expand on this, consider also the rate of elimination and the time it takes to probe an offset:

For your first guess, you may be able to eliminate half of the entire data store, say a billion events.
On your second guess, you may even eliminate an additional quarter: five hundred million.
If it takes a second to complete each of those probes, then your information gain definitely exceeds your cost.

Within the next 20 probes, you could potentially narrow the range of possibilities to 2000 events at a cost of, say, 20 seconds.

To precisely locate the lower bound could cost another 11 seconds. However, depending on the batch size of your driver,
you may already be streaming 500 events at a time.  Simply iterating all of the remaining 2000 events could be completed in far less time.

As a result, you want an algorithm with both seek and scanning capability, transitioning from seek to scanning at
some point in the search. In practice, this threshold is around 3-5000 messages depending on the size of the cluster and the availability of the data.

## Implementation

Since Jet.com uses F\# as its predominant language, there are a wide variety of options available to query both EventStore and Kafka, 
as well as to operate on Json formatted messages.
Designing sophisticated user interfaces is also quite easy with F\# and [Xaml](https://en.wikipedia.org/wiki/Extensible_Application_Markup_Language).

#### JsonPath

Querying a Json document using JsonPath makes it easy for an end user to specify different `target` fields depending on their needs.  I've
[written](/entry/2016/12/23/jsonpath-queries-using-fsharpdata) extensively about this, and designed a quick JsonPath [library](https://github.com/j-alexander/FSharp.Data.JsonPath) for use from F\#.

#### Streaming

A selection of clients exist for streaming data:

- EventStore's official [.NET Client](http://docs.geteventstore.com/dotnet-api)
- Kafka support from a number of clients, including:
   - [Kafunk](http://jet.github.io/kafunk/) for F\#, under development.
   - [RdKafka](https://github.com/edenhill/librdkafka) wrappers:
      - [by Andreas Heider](https://github.com/ah-/rdkafka-dotnet) is stable, but no longer maintained.
      - [by Confluent](https://github.com/confluentinc/confluent-kafka-dotnet) replaces it, but is _pre-release_.
   - [Microsoft C\# Client](https://github.com/Microsoft/CSharpClient-for-Kafka) is an older client in the same family as Jet's original Marvel client.
   - [kafka-net](https://github.com/Jroland/kafka-net) a client by James Roland.

In this application, I used the [Nata.IO](https://github.com/j-alexander/nata) library, which provides a common abstraction over stable
versions of the above clients. It includes a consistent mechanism to query the range of available offsets, as well as to stream from arbitrary positions
in the target event log.


#### User Interface

To build a modern WPF user interface in F\#, I also used the [FsXaml](http://fsprojects.github.io/FsXaml/tutorial.html) library.
Although the documentation is sparse, it is a powerful tool with nearly the same flexibility and capability as the well-known C\# tooling in Visual Studio.
In fact, it supports the same interactive visual designer for Xaml.

A video screencast of the final interface:

<video controls poster="Screencast-Poster.png">
  <source src="Screencast.mp4" type="video/mp4"/>
</video>

### Feature Overview

Many of these features work together to solve the problem outlined above.  These include:

- Selecting either Kafka or Eventstore with custom host name, and topic or stream.
- Targetting a `Timestamp`, `Text` or `Numeric` value from a field that increases over time:
   - Custom JsonPath queries such as `$.envelope.timestamp` to extract this field.
   - Custom transition threshold from `Seek` to `Scan`.

   [![Updated Query Control](SelectTargetType.png)](SelectTargetType.png)

- Full controls to Start, Stop and Resume multiple searches simultaneously.
- Visibility into the complete search state and progress.
- Copy/Paste support into Excel for subsequent analysis.

### Source Code

- **Algorithm**

   The [search algorithm](https://github.com/j-alexander/binary-log-search/blob/master/Algorithm/BinaryLogSearch.fs)
   is initialized with [connection information](https://github.com/j-alexander/binary-log-search/blob/master/Algorithm/Connection.fs#L9-L11)
   to query the `indexOf` bounds within the topic or stream, and `readFrom` arbitrary positions within those bounds.
   When asked to [execute](https://github.com/j-alexander/binary-log-search/blob/master/Algorithm/BinaryLogSearch.fs#L17) it
   performs the search and emits periodic updates to the [search state](https://github.com/j-alexander/binary-log-search/blob/master/Algorithm/SearchState.fs).
   Extraction of the `target` is performed using a [codec](https://github.com/j-alexander/binary-log-search/blob/master/Algorithm/Codec.fs)
   that transforms a raw byte stream into a stream of the desired Json field.

   This algorithm started its life as a simple F\# `.fsx` script.

- **User Interface**

   Originally created using C\#, the [FsXaml](http://fsprojects.github.io/FsXaml/index.html) UI retains all of the original design except for some unneeded
   code-behind (e.g. `*.xaml.cs`).

   Models use dependency properties to expose [search state](https://github.com/j-alexander/binary-log-search/blob/master/UI/Models/SearchModel.fs)
   and [query connection settings](https://github.com/j-alexander/binary-log-search/blob/master/UI/Models/QueryModel.fs)
   for binding.  Views are expressed using the [exact same XAML](https://github.com/j-alexander/binary-log-search/blob/master/UI/Views/MainWindow.xaml)
   as the original C\# UI, including a composition of user controls for [entering connection settings](https://github.com/j-alexander/binary-log-search/blob/master/UI/Views/QueryView.xaml)
   and displaying [the grid of search data](https://github.com/j-alexander/binary-log-search/blob/master/UI/Views/SearchView.xaml).  Additional
   view-models provide the binding and commands for the application to [manage the searches](https://github.com/j-alexander/binary-log-search/blob/master/UI/ViewModels/SearchViewModel.fs)
   or [connect](https://github.com/j-alexander/binary-log-search/blob/master/UI/ViewModels/QueryViewModel.fs) to data stores.

   The primary difference in F\# is the use of [type providers](https://github.com/j-alexander/binary-log-search/blob/master/UI/Views/Views.fs) to generate type
   information from the XAML to use in subsequent F\# code.

   Once assembled, the [application](https://github.com/j-alexander/binary-log-search/blob/master/UI/App.fs) is wired up and compiled as an F\# Windows Application.

    [![F\# Windows Application](FSharpWindowsApplication.png)](FSharpWindowsApplication.png)

- **Life-cycle**

   To better manage the end-user experience of the application over time, I also created a [WiX Toolset](http://wixtoolset.org/)
   [setup file](https://github.com/j-alexander/binary-log-search/blob/master/Setup/Product.wxs) to create installers.
   
   This is paired with a user control to [query the latest version](https://github.com/j-alexander/binary-log-search/blob/master/UI/Models/VersionModel.fs) and
   provide an unobtrusive notification in the event of a new release.

    [![Display Upgrade Notice](DisplayUpgradeNotice.png)](DisplayUpgradeNotice.png)


### Summary

The resulting application demonstrates how a simple and clean F\# stream-processing algorithm can be exposed with a complete and modern user inteface.
It pulls together technology for EventStore and Kafka, query capability with JsonPath, view layout with `FsXaml` type providers, and a host of other
elements designed to make this kind of application easy.

It _performs extremely well_.  In scenarios such as the screencast above, it can locate the lowerbound of a timestamp among four million messages in less
than three seconds.  Moreover, the logarithmic nature of the algorithm lets us scale to _many many times_ that amount of data with a few extra seek operations.
For example, *a billion events* requires only 8 extra seeks (typically on the order of a few milliseconds each).

#### Resources

To play with the source code or download a copy, visit me on GitHub:

[https://github.com/j-alexander/binary-log-search](https://github.com/j-alexander/binary-log-search)

_Note that while I've used this tool at Jet, the company makes no warranty with respect to its use in your own environment.  It's open source.  Review and edit to
meet your own unique needs!  And have **fun**!_

If you'd like to learn more about technology careers at Jet.com, visit [https://tech.jet.com/](https://tech.jet.com/). ☺