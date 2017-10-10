(**
## Objective

[F#](http://fsharp.org/about/index.html) has been characterized as a modern programming language
well-suited to data-rich analytical problems.
In this language, interacting with diverse datasets from event streams, web services, and databases
is further simplified with the open-source [FSharp.Data](http://fsharp.github.io/FSharp.Data/)
library.

Many of these real-world documents are hierarchically structured in Json and Xml.
When designing reusable applications and algorithms in this space, it becomes _very important_ to
be able to extract subsets of data using custom queries.

For [Xml](https://en.wikipedia.org/wiki/XML), [XPATH](http://www.w3schools.com/xml/xml_xpath.asp) is
a well established tool for describing specific elements within a document.

A similar mechanism exists for Json, called **[JsonPath](http://goessner.net/articles/JsonPath/)**.

Using JsonPath with F# typically involves converting data to 
[Newtonsoft.Json](http://www.newtonsoft.com/json/help/html/QueryJsonSelectToken.htm)'s `JObject`
type and invoking `SelectToken`.  However, FSharp.Data has an elegant and functional representation
of [JsonValue](http://fsharp.github.io/FSharp.Data/reference/fsharp-data-jsonvalue.html)s that is 
very widely used.

In this article, I describe _one possible way_ to directly query subsets of a `JsonValue` document
without converting your entire dataset to perform some extraction.
*)
(*** hide ***)
open System
open System.Text
open System.Text.RegularExpressions
(**
## Dependencies
1. *FSharp.Data* from [NuGet](https://www.nuget.org/packages/newtonsoft.json/), or using [Paket](https://fsprojects.github.io/Paket/):

 ```powershell
   ./.paket/paket.exe add nuget FSharp.Data project MyProject
 ```

2. *Reference* and open the FSharp.Data library:
*)
#r "packages/FSharp.Data/lib/net45/FSharp.Data.dll"
open FSharp.Data
(*** hide ***)
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module JsonPath =

    let private valueOr (defaultValue:int) =
        function Some x -> x | None -> defaultValue

(**
## Query
Given an arbitrary JsonPath query string, we want to derive a structured representation of
that query.
*)
    type Query = string

    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    module Query =
(**
The query consists of a sequence of levels from the root of a json document `$` separated
by the `.` character.  Each level refers to a named property or array, and each case is
scoped to match that `Exact` level, or inclusively `Any` instances within its subtree.
If a level refers to an array, then a slice, literal index, or wildcard operation (between
`[` and `]`) specifies which elements of an array match the query.
*)
        type Levels = Level list
        and Level = Scope * Type
        and Type = Property of Name | Array of Index
        and Scope = Any | Exact
        and Name = string
        and Index =
            | Expression of string
            | Wildcard
            | Slice of start:int option * finish:int option * step:int
            | Literal of int list
(**
Given a `Query` string, we should be able to obtain a pattern for the `Levels`
which can satisfy (or _match_) that query.
*)
        let levelsFor : Query -> Levels =
            // ... 
(*** hide ***)
            fun (path:string) ->
                []
(**
For example:

```fsharp
  // any author
  "$..author"   [Any,Property("author")]

  // anything at the root of the store
  "$.store.*"   [Exact,Property("store");Exact,Property("*")]

  // the last book in any collection of books
  "$..book[-1]" [Any,Property("book");Exact,Array(Index.Literal[-1])]
```

The implementation of this parsing operation is pretty straightforward and can be found
[here](https://github.com/j-alexander/FSharp.Data.JsonPath/blob/24fb0ef8dde0a7e4a9c9f87685b53dc64e9ed786/FSharp.Data.JsonPath/JsonPath.fs#L35-L94)
for the version being discussed in this post.
*)
(**
## Pattern Matching
Here, `Pattern` matching on the `Query` is performed using immutable [non-deterministic
finite automata](https://en.wikipedia.org/wiki/Nondeterministic_finite_automaton).
*)
    module Pattern  =
(**
An automaton is a specialized kind of state machine.  When given a sequence of
inputs, it has the potential to arrive at some final state indicating acceptance of those
inputs.

Imagine some user interface has a text box that accepts several possible passwords. Each
letter you input will _transition_ your automaton from one state to another (like a
combination lock). If you input the right sequence of letters, you will arrive at a
"password accepted" state and successfully log into the app.

In F#, we can represent this concept as follows:

### Automata
First, more than _one automaton_ is called _automata_.
*)
        type Automata = Automaton list
(**
* _Deterministic_ finite automata will yield exactly one state when given an input.
* _Non-deterministic_ finite automata, on the other hand, may yield more than one. Recall
that our password example would match several possible passwords at once.
*)
        and Automaton = Input->State list
(**
Given some input:

* One possible state of the automaton is a `Match` or _acceptance_ of 
the input.
* If no match occurs, the automaton must transition to a new state ready to
accept more input.
  * For example, if you enter the first letter `s` of a password "secret", the new state of
your automaton must stand ready to _accept_ the character sequence `[e;c;r;e;t]`.

These automata are _immutable_ in a functional programming sense.  Rather than mutating
some internal state, an input produces derivations.  As a consequence, an input could
produce a `Match` state, a _new_ `Automaton`, or even a combination of these.
*)
        and State = Match | Automaton of Automaton
(**
Finally, since we're matching json documents rather than passwords, your input from an
actual document could be either:

* a `Property` of a [JsonValue.Record](https://github.com/fsharp/FSharp.Data/blob/a2c25cc5557a4a3918e4d22e191ee74e15f56b49/src/Json/JsonValue.fs#L38-38) with some `Name`, or
* an `Array` element of a [JsonValue.Array](https://github.com/fsharp/FSharp.Data/blob/a2c25cc5557a4a3918e4d22e191ee74e15f56b49/src/Json/JsonValue.fs#L39-39) at some specific index
*)
        and Input =
            | Property of Query.Name
            | Array of index:int*length:int
(**
Where are the other `JsonValue` cases? These are _values_ that occur at some path in the
JsonPath query, rather than parts of the path itself.

Given the following example, we might refer to JsonPath "`$.x`". The value "`3`" would
be the _result_ of this match.

```json
  { "x": 3 }
```

### Example

Suppose we're given the query "`$..book[-2]`" by a user looking for the second-last
book in any collection of a Json document.

Using our structured query format, `Query.Levels`, we obtain:

```fsharp
  [Any,Property("book");Exact,Array(Index.Literal[-2])]
```

Now let's suppose that our datastore has a document like this:

```json
{ "store":
  { "books":
    [ { "author": "Jonathan", "title": "RdKafka for F# Microservices" },
      { "author": "Jonathan", "title": "JsonPath Queries using FSharp.Data" }, 
      { "author": "Jonathan", "title": "Binary Log Search" } ]},
    "movies": [] } }
```

You could imagine that it conforms to the following schema diagram.  Moreover,
the structured query format might be represented using the automaton to
its right.

![Automaton: Second-last Book](book-store-json.gif)

In the above example, we enter the automaton at the root of the json document `$` in
the upper left.  If the root document has no `books` property, we follow the `..` epsilon
transition back to that first state - waiting for some portion of the json document that
does have a `books` property.  If some node _does_ have a `books` property, we follow
it to the middle state.  If that property is an array with index `[-2]`, then we
transition to `Accept` the value at this index.

Consider quickly the following json document:
```json
{ "books": { "books": [ 0, "success", { "books": 3 } ]} }
```

From the starting state, you follow both the possibility that books is an array with
a second last element, but also, that books transitions back to the start state and
matches with some child `books` array inside that first `books` property.

### Transition Fun

Since the current `State` of an Automaton takes arbitrary json `Input`, and
produces a new collection of `States`, this transition can be written recursively.  The
function below interprets `Input` differently depending on the `Query` path level 
we're currently trying to satisfy.  This models the _structure_ of the automaton for
a given `Query`.

The cases are as follows:
*)          
        let rec transition (levels:Query.Levels) : Automaton =
            match levels with
            
            // 1. nothing to match matches nothing
            | [] -> fun _ -> []

            // 2. looking for a property of name n with scope s
            | (s,Query.Property(n)) :: tail ->
                function
                | Input.Array _ -> []
                | Input.Property name ->
                    // 2a) does the input property name match?
                    match name with
                    | x when x=n || "*"=n ->
                        match tail with
                        // if nothing remains to match,
                        // we accept this value
                        | [] -> [ Match ]
                        // otherwise, we continue to take input
                        // for the rest of our query
                        | xs -> [ Automaton (transition xs) ]
                    | _ -> []
                    @
                    // 2b) does the scope also include
                    //     anything in the subtree?
                    match s with
                    | Query.Any -> [ Automaton (transition levels) ]
                    | Query.Exact -> []
                
            // 3. looking for an array with a matching index
            | (s,Query.Array(i)) :: tail ->
                function
                | Input.Property _ -> []
                | Input.Array (index,length) ->
                    match i with

                    // 3a) querying for all indices?
                    | Query.Index.Wildcard ->
                        match tail with
                        // if nothing remains in the query,
                        // we accept this value
                        | [] -> [ Match ]
                        // otherwise, we continue to take input
                        // for the rest of our query
                        | xs -> [ Automaton (transition xs) ]

                    // 3b) querying for specific indices?
                    | Query.Index.Literal xs when
                        (xs // also handle valid negative index literals:
                         |> List.map (function x when x < 0 -> length+x | x -> x)
                         |> List.exists ((=) index)) ->
                        match tail with
                        // if nothing remains in the query,
                        // we accept this value
                        | [] -> [ Match ]
                        // otherwise, we continue to take input
                        | xs -> [ Automaton (transition xs) ]
                        
                    // 3c) querying for an index range (with step)?
                    //     handle negative indices as well
                    | Query.Index.Slice(start,finish,step) when (step > 0) ->
                        let start =
                            match start |> valueOr 0 with
                            | x when x < 0 -> length+x
                            | x -> x
                        let finish =
                            match finish |> valueOr length with
                            | x when x <= 0 -> length+x
                            | x -> x
                        if (finish > index && index >= start) &&
                           (0 = (index-start) % step) then
                            match tail with
                            | [] -> [ Match ]
                            | xs -> [ Automaton (transition xs) ]
                        else []
                    | _ -> []
                    @
                    match s with
                    // does the scope also include
                    // anything in the subtree?
                    | Query.Any -> [ Automaton (transition levels) ]
                    | Query.Exact -> []
(**
The starting state for the automaton is a function accepting input
matching the current query:
*)
        let create (levels:Query.Levels) : State =
            Automaton (transition levels)
(**
So far, we've created a structured representation of an arbitrary JsonPath query.  We've also
defined state transitions based on that representation.  These give us automata capable
of matching any user-defined query.

### Search

Next, we'll look at actually running the search sequence for a given automaton and json document.

Finding a sequence of matching json values can also be defined recursively.  Here, `findSeq` is
a function accepting a list of current states and some json value (effectively, our _positions_ in
the state machine and document).
*)
    let findSeq = 

        let partition =
            List.fold (fun (matches, automata) -> function
                | Pattern.State.Match -> true, automata
                | Pattern.State.Automaton x -> matches, x :: automata) (false, [])

        let rec recurse = function
            | [] -> Seq.empty
            | (states:Pattern.State list, value:JsonValue) :: positions ->
(**
This representation works especially well with long linear datastructures that might otherwise
cause a `StackOverflowException`. For example:

```json
 { "100000":
   { "99999":
     { "99998":
       { "99997": 
         // ...
           { "2":
             { "1":
               { "0": null } } }
         // ...
       } } } }
```

If any of the states is a `Match` state, we yield the current `JsonValue`.  But also, we can
continue to check the remaining automata for matches:
*)  
                seq {
                    let hasMatch, automata = partition states
                    if hasMatch then
                        yield value

                    yield!
                        match value with
                        // (1) for a record, we apply the property name to the
                        //     automata to obtain new states, and recurse:
                        | JsonValue.Record xs ->
                            xs
                            |> Array.map(fun (name,json) ->
                                automata
                                |> List.collect(fun a -> 
                                    a (Pattern.Input.Property name)),
                                json)
                        // (2) for an array, we apply the index values to the
                        //     automata to obtain new states, and recurse:
                        | JsonValue.Array xs ->
                            xs
                            |> Array.mapi(fun i json ->
                                automata
                                |> List.collect(fun a ->
                                    a (Pattern.Input.Array(i,xs.Length))),
                                json)
                        | _ -> Array.empty
                        |> Array.foldBack (fun x xs -> x :: xs) <| positions
                        |> recurse
                }  
(**
We also handle `$.` as a unique case for matching the document itself:
*)   
        Query.levelsFor >> function
        | [Query.Exact,Query.Property ""] -> Seq.singleton
        | (Query.Exact,Query.Property "")::levels
        | levels ->
            let start = Pattern.create levels
            fun json -> recurse[[start],json]
(**
And for convenience, we add a few different mechanisms to eagerly evaluate the
search, or optionally obtain the first match case:
*)
    let findList query =
        findSeq query >> Seq.toList

    let find query =
        findSeq query >> Seq.head

    let tryFind query =
        findSeq query >> Seq.tryPick Some
(*** hide ***)
let jsonSeq : seq<JsonValue> = Seq.empty
(**
### Use
Searching for a list of JsonValue matches within each document of a sequence is then
quite simple.  For instance, if a product data source has multiple images, the first
image is usually the main image (index 0).  Suppose we want to query
for the md5 hash of any _alternative_ images (index 1+):
*)
let count =
    // partially applying the query runs parsing only once
    // obtain a total count of all matches:
    jsonSeq
    |> Seq.map (JsonPath.findList "$.source.data.images[1:].md5" >> List.length)
    |> Seq.sum
(**
### Correctness

_What does JsonPath support that this solution does not?_

In particular, you might wonder about custom expression support. For instance,
filtering books with `$.books[?(@.price<10)]`, or `$.books[?(@.Name == 'Json for Dummies')]`.

These can definitely be added -- and would make for a nice follow-up post!

_Is this implementation correct?_

[Unit Tests](https://github.com/j-alexander/nata/blob/8e70167e3ae710faddb277ef981d300017f2f5c0/Nata.Fun.JsonPath.Tests/JsonValueTests.fs#L17)
have been created, encompassing a wide variety of cases.
Special attention was paid to array slice and literal operations, in addition to some
of the more unusual cases: long [cons-lists](https://en.wikipedia.org/wiki/Cons#Lists),
for instance.

In many cases, implementations of JsonPath disagree! ;)

### Performance

For a comparison with [Newtonsoft's Json.NET library](http://www.newtonsoft.com/json/help/html/QueryJsonSelectTokenWithLinq.htm),
several queries were applied to an in-memory dataset of 100,000 products of varying
complexity and size. In particular:

* `$.no.match` - evaluates early termination in the event no match exists
* `$.source.data.images[1:].md5` - multiple elements of an array at a _specific path_
* `$..sku_id` - occurs many times in the document at varying locations
* `$..title` - similar to `sku_id`, but appears in records with many properties

Measurements are taken starting from either `string` or `JsonValue`.  In particular, 
the `JsonPath.find` functions need to prepend `JsonValue.Parse >>` when reading a
sequence of strings.  In the opposite direction, `JsonValue` is converted to string with
the `JsonSaveOptions.DisableFormatting` flag applied before using Newtonsoft's Json.NET.

The intention is to show which library might perform better given data in a particular
format.

![Comparison w/Newtonsoft.Json](benchmarks.png)

In the results above, one takeaway is quite clear: if your data is already in `JsonValue` format,
we've created a mechanism that can query paths quite quickly.  Operations that must query
the entire document have very predictable performance.  However, querying well-defined
paths definitely speeds up the search.

By contrast, if you're operating on Json data exclusively in string format, _you should
probably stay with Newtonsoft.Json_.

### Summary

In this article, I describe _one possible way_ to directly query subsets of a `JsonValue`
document without converting it to an alternate type.  This makes it easier to work with
the rich variety of datasources available in `FSharp.Data` in JsonValue format.

This approach is especially useful for ad-hoc queries defined by a user interacting with
an existing source of JsonValue data.

As a plus, we also had some fun with non-deterministic finite automata in F#!  :D
*)