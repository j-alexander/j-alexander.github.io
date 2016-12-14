(**
## Objective
*)
(*** hide ***)
open System
open System.Text
open System.Text.RegularExpressions
(**
## Dependencies
1. *FSharp.Data* from NuGet, or using Paket:

 ```powershell
   ./.paket/paket.exe add nuget FSharp.Data project MyProject
 ```

2. *Reference* and open the FSharp.Data library:
*)
#r "packages/FSharp.Data/lib/net40/FSharp.Data.dll"
open FSharp.Data
(*** hide ***)
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module JsonValue =

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
Finally, given a `Query` string, we should be able to obtain a pattern for the `Levels`
which can satisfy (or _match_) the query.
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

*)
(**
## Pattern Matching
Here, `Pattern` matching on the `Query` is performed using immutable [non-deterministic
finite automata](https://en.wikipedia.org/wiki/Nondeterministic_finite_automaton).
*)
    module Pattern  =
(**
An automaton is a specialized kind of state machine that when given a sequence of
inputs has the potential to arrive at some final state indicating acceptance of those
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
some internal state, an input produces derivations.  As a consequence, an input produces
a `Match` state or a _new_ `Automaton` with that input applied.
*)
        and State = Match | Automaton of Automaton
(**
Finally, since we're matching json documents rather than passwords, your input from an
actual document could be either:

* a `Property` of a [JsonValue.Record](https://github.com/fsharp/FSharp.Data/blob/master/src/Json/JsonValue.fs#L38-38) with some `Name`, or
* an `Array` element of a [JsonValue.Array](https://github.com/fsharp/FSharp.Data/blob/master/src/Json/JsonValue.fs#L39-39) at some specific index
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

Our implementation of the state machine starts with the structured representation
of a query from above, that is `Query.Levels`.  Since the current `State` of an
Automaton takes arbitrary json `Input`, and produces a new collection of `States`
it can be written recursively.

The cases are as follows:
*)          
        let rec transition (levels:Query.Levels) : Automaton =
            match levels with
            
            // nothing to match matches nothing
            | [] -> fun _ -> []

            // looking for a property of name n with scope s
            | (s,Query.Property(n)) :: tail ->
                function
                | Input.Array _ -> []
                | Input.Property name ->
                    // does the input property name match?
                    match name with
                    | x when x=n || "*"=n ->
                        match tail with
                        // if nothing remains to match,
                        // we accept this value
                        | [] -> [ Match ]
                        // otherwise, we continue to take input
                        | xs -> [ Automaton (transition xs) ]
                    | _ -> []
                    @
                    // does the scope also include
                    // anything in the subtree?
                    match s with
                    | Query.Any -> [ Automaton (transition levels) ]
                    | Query.Exact -> []
                
            // looking for an array with a matching index
            | (s,Query.Array(i)) :: tail ->
                function
                | Input.Property _ -> []
                | Input.Array (index,length) ->
                    match i with

                    // querying for all indices?
                    | Query.Index.Wildcard ->
                        match tail with
                        // if nothing remains in the query,
                        // we accept this value
                        | [] -> [ Match ]
                        // otherwise, we continue to take input
                        | xs -> [ Automaton (transition xs) ]

                    // querying for specific indices?
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
                        
                    // querying for an index range (with step)?
                    // -- handle negative indices as well
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
(*** hide ***)


    let findSeq = 

        let rec recurse (states:Pattern.State list,value:JsonValue) =
            let isMatch, automata =
                states
                |> List.exists (function
                    | Pattern.State.Match -> true
                    | _ -> false),
                states
                |> List.choose (function
                    | Pattern.State.Automaton x -> Some(x)
                    | _ -> None)
            seq {
                if isMatch then
                    yield value
                yield!
                    match value with
                    | JsonValue.Record xs ->
                        xs
                        |> Seq.map(fun (name,json) ->
                            automata
                            |> List.collect(fun a -> 
                                a (Pattern.Input.Property name)),
                            json)
                        |> Seq.collect recurse
                    | JsonValue.Array xs ->
                        xs
                        |> Seq.mapi(fun i json ->
                            automata
                            |> List.collect(fun a ->
                                a (Pattern.Input.Array(i,xs.Length))),
                            json)
                        |> Seq.collect recurse
                    | _ -> Seq.empty         
            }
                
        Query.levelsFor >> function
        | [Query.Exact,Query.Property ""] -> Seq.singleton
        | (Query.Exact,Query.Property "")::levels
        | levels ->
            let start = Pattern.create levels
            fun json -> recurse([start],json)
            
    let findList query =
        findSeq query >> Seq.toList

    let find query =
        findSeq query >> Seq.head

    let tryFind query =
        findSeq query >> Seq.tryPick Some