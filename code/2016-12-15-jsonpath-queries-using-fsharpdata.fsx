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
##Query
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
`[` and `]` specifies which elements of an array match the query.
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
(*** hide ***)
            fun (path:string) ->
                []

    [<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
    module Pattern  =

        type Automata = Automaton list
        and Automaton = Input->State list
        and State = Match | Automaton of Automaton
        and Input =
            | Property of Query.Name
            | Array of index:int*length:int

        let rec transition (levels:Query.Levels) =
            match levels with
            | [] -> fun _ -> []

            | (q,Query.Property(n)) :: tail ->
                function
                | Input.Array _ -> []
                | Input.Property name ->
                    match name with
                    | x when x=n || "*"=n ->
                        match tail with
                        | [] -> [ Match ]
                        | xs -> [ Automaton (transition xs) ]
                    | _ -> []
                    @
                    match q with
                    | Query.Any -> [ Automaton (transition levels) ]
                    | Query.Exact -> []
                
            | (q,Query.Array(p)) :: tail ->
                function
                | Input.Property _ -> []
                | Input.Array (index,length) ->
                    match p with
                    | Query.Index.Wildcard ->
                        match tail with
                        | [] -> [ Match ]
                        | xs -> [ Automaton (transition xs) ]
                    | Query.Index.Literal xs when
                        (xs
                         |> List.map (function x when x < 0 -> length+x | x -> x)
                         |> List.exists ((=) index)) ->
                        match tail with
                        | [] -> [ Match ]
                        | xs -> [ Automaton (transition xs) ]
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
                    | Query.Index.Expression _ -> []
                    @
                    match q with
                    | Query.Any -> [ Automaton (transition levels) ]
                    | Query.Exact -> []
                    
        let create (levels:Query.Levels) : State =
            Automaton (transition levels)


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