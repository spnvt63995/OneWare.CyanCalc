# CyanGraph for OneWare

Choose **View → CyanGraph** to open the graph document alongside an existing workbook. Choosing the command again focuses the open graph. In the document's **Connection → Connection settings…** dialog, enter a Bolt or Neo4j URI, database name and credentials for a read-only database.

## Follow a Movies credit

Enter this query in **Statement**, choose **Run**, then **Graph + Table**.

```cypher
MATCH (actor:Person)-[credit:ACTED_IN]->(film:Movie {title:'The Matrix'})
RETURN actor, credit, credit.roles AS roles, film ORDER BY actor.name
```

In the official Movies sample, five rows share one film. Select the arrow from Carrie-Anne Moss to The Matrix. The inspector shows the endpoints, **roles → ["Trinity"]**, and the element ID. Row 1 of **credit** is highlighted. **Full value…** opens the role list in a dialog owned by the current window. **Raw** shows the same rows, node IDs and directed relationships.

Switch between the workbook and graph tabs to keep the current worksheet cell, graph query, parameters and selected credit. Closing the graph tab cancels its current query, drains any secondary result and releases the Neo4j driver. **View → CyanGraph** opens a fresh disconnected document. Saved questions and run history use `CyanGraph/questions.json` under OneWare's application data directory. The standalone app has its own profile.

## Build and load the local preview

Fetch the public source revisions in `dependencies.json`. The graph module takes the graph view, grid and Neo4j project from `.deps/CyanGraph`. The workbook uses its own CyanCalc revision. The graph view targets the host's Avalonia 11.3.22.

```sh
python3 tools/fetch-dependencies.py
dotnet build OneWare.CyanGraph.slnx -c Release
dotnet run --project tests/OneWare.CyanGraph.Checks -c Release --no-build
python3 tools/package-graph.py --cyan-graph-root .deps/CyanGraph --output artifacts/OneWare.CyanGraph
```

In Rider, open `OneWare.CyanGraph.slnx` after fetching dependencies. Build the solution and run **OneWare.CyanGraph.Checks**.

Pass the staged module directory to OneWare Studio 1.0.45 when starting a local review instance.

```sh
/path/to/OneWareStudio --modules "$PWD/artifacts/OneWare.CyanGraph" \
  --oneware-dir "$PWD/artifacts/graph-documents" --oneware-appdata-dir "$PWD/artifacts/graph-profile"
```

For a workbook and graph in the same instance, stage both modules in the review module directory with the current shared `CyanCalc.Grid.dll`. The graph package contains its module, graph view, Neo4j query library and driver, grid and license notices. The host provides Avalonia and the docking services.
