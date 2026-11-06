using RESTTest.Models;
using RESTTest.Services;
using RESTTest.Services.Importing;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

public class CollectionImporterTests
{
    private static ImportedCollection Import(string json)
    {
        Assert.True(CollectionImporter.TryImport(json, out var result, out var error), error);
        return result!;
    }

    private static string Fail(string text)
    {
        Assert.False(CollectionImporter.TryImport(text, out var result, out var error));
        Assert.Null(result);
        return error!;
    }

    private static IEnumerable<NodeModel> Requests(IEnumerable<NodeModel> nodes) =>
        nodes.SelectMany(n => n.Kind == NodeKind.Request ? [n] : Requests(n.Children));

    private static RequestModel Find(ImportedCollection import, string name) =>
        Requests(import.Items).First(n => n.Name == name).Request!;

    // ---- Recognition and errors ----------------------------------------------------------------

    [Fact]
    public void Garbage_and_empty_files_are_explained()
    {
        Assert.Contains("empty", Fail("  "));
        Assert.Contains("not a JSON file", Fail("hello"));
        Assert.Contains("not valid JSON", Fail("{ nope"));
        Assert.Contains("not a Postman v2 collection", Fail("""{"hello": 1}"""));
    }

    [Fact]
    public void Yaml_is_recognised_and_the_user_is_told_what_to_do()
    {
        Assert.Contains("YAML", Fail("openapi: 3.0.0\ninfo:\n  title: x\n"));
    }

    [Fact]
    public void A_postman_environment_file_gets_its_own_hint()
    {
        Assert.Contains("environment", Fail("""{"_postman_variable_scope":"environment","values":[]}"""));
    }

    [Fact]
    public void A_collection_with_no_requests_is_refused()
    {
        Assert.Contains("no requests", Fail("""{"info":{"name":"x"},"item":[{"name":"empty folder","item":[]}]}"""));
    }

    [Fact]
    public void A_byte_order_mark_does_not_hide_the_file()
    {
        Import("\uFEFF" + """{"info":{"name":"x"},"item":[{"name":"a","request":"https://h.test/a"}]}""");
    }

    // ---- Postman -------------------------------------------------------------------------------

    private const string Postman = """
    {
      "info": { "name": "Shop API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
      "variable": [ { "key": "baseUrl", "value": "https://api.shop.test/v1" }, { "key": "id", "value": "42" } ],
      "auth": { "type": "bearer", "bearer": [ { "key": "token", "value": "root-token" } ] },
      "item": [
        { "name": "Users", "item": [
          { "name": "List users",
            "event": [ { "listen": "test", "script": { "exec": [ "pm.test('ok', () => {});" ] } } ],
            "request": { "method": "GET",
              "header": [ { "key": "Accept", "value": "application/json" }, { "key": "X-Off", "value": "1", "disabled": true } ],
              "url": { "raw": "{{baseUrl}}/users?page=2&sort=name",
                       "query": [ { "key": "page", "value": "2" }, { "key": "sort", "value": "name", "disabled": true } ] } } },
          { "name": "Create user",
            "request": { "method": "POST",
              "auth": { "type": "basic", "basic": [ { "key": "username", "value": "ada" }, { "key": "password", "value": "pw" } ] },
              "header": [ { "key": "Content-Type", "value": "application/json" } ],
              "body": { "mode": "raw", "raw": "{\"id\": {{id}}}" },
              "url": "{{baseUrl}}/users" } }
        ] },
        { "name": "Login form",
          "request": { "method": "POST", "auth": { "type": "noauth" },
            "body": { "mode": "urlencoded", "urlencoded": [ { "key": "user", "value": "a b" }, { "key": "x", "value": "1", "disabled": true } ] },
            "url": { "raw": "https://api.shop.test/v1/login", "protocol": "https", "host": ["api","shop","test"], "path": ["v1","login"] } } },
        { "name": "Upload",
          "request": { "method": "PUT",
            "body": { "mode": "formdata", "formdata": [ { "key": "note", "value": "hi", "type": "text" }, { "key": "doc", "type": "file", "src": "C:\\temp\\a.pdf" } ] },
            "url": "{{baseUrl}}/files/:id", "description": "x" } },
        { "name": "Other host", "request": { "method": "GET", "url": "https://other.test/ping" } },
        { "name": "Oauth", "request": { "method": "GET", "auth": { "type": "oauth2" }, "url": "{{baseUrl}}/me" } },
        { "name": "Typed", "request": { "method": "POST", "body": { "mode": "raw", "raw": "<a/>", "options": { "raw": { "language": "xml" } } }, "url": "{{baseUrl}}/x" } },
        { "name": "Missing", "request": { "method": "GET", "url": "{{unknownHost}}/z/{{token}}" } },
        { "name": "Gql", "request": { "method": "POST", "body": { "mode": "graphql", "graphql": { "query": "{ me { id } }", "variables": "{\"a\":1}" } }, "url": "{{baseUrl}}/graphql" } }
      ]
    }
    """;

    [Fact]
    public void Postman_folders_requests_and_the_name_come_across()
    {
        var import = Import(Postman);

        Assert.Equal("Postman", import.Format);
        Assert.Equal("Shop API", import.Name);
        Assert.Equal(["Users", "Login form", "Upload", "Other host", "Oauth", "Typed", "Missing", "Gql"], import.Items.Select(i => i.Name));
        Assert.Equal(["List users", "Create user"], import.Items[0].Children.Select(c => c.Name));
        Assert.Equal(9, import.RequestCount);
        Assert.Equal(1, import.FolderCount);
    }

    [Fact]
    public void Postman_base_url_variable_becomes_the_collection_base_url_and_paths_are_relative()
    {
        var import = Import(Postman);

        var main = import.Environments[0];
        Assert.Equal("baseUrl", main.Name);
        Assert.Equal("https://api.shop.test/v1", main.BaseUrl);

        Assert.Equal("/users", Find(import, "List users").Path);
        // A full URL on the same base (typed out, not through the variable) is shortened too.
        Assert.Equal("/login", Find(import, "Login form").Path);
    }

    [Fact]
    public void Postman_query_rows_keep_their_enabled_state_and_leave_the_path_clean()
    {
        var request = Find(Import(Postman), "List users");

        Assert.Equal(["page", "sort"], request.QueryParams.Select(q => q.Key));
        Assert.Equal([true, false], request.QueryParams.Select(q => q.Enabled));
        Assert.Equal("/users", request.Path);
        Assert.Equal([true, false], request.Headers.Select(h => h.Enabled));
    }

    [Fact]
    public void Postman_auth_is_inherited_overridden_and_cleared()
    {
        var import = Import(Postman);

        var inherited = Find(import, "List users");
        Assert.Equal(AuthKind.BearerToken, inherited.Auth);
        Assert.Equal("root-token", inherited.BearerToken);

        var own = Find(import, "Create user");
        Assert.Equal(AuthKind.Basic, own.Auth);
        Assert.Equal("ada", own.BasicUsername);
        Assert.Equal("pw", own.BasicPassword);

        Assert.Equal(AuthKind.None, Find(import, "Login form").Auth);
    }

    [Fact]
    public void Postman_bodies_map_to_the_matching_body_kinds()
    {
        var import = Import(Postman);

        var json = Find(import, "Create user");
        Assert.Equal(BodyKind.Json, json.BodyKind);
        Assert.Equal("""{"id": 42}""", json.Body);

        var form = Find(import, "Login form");
        Assert.Equal(BodyKind.FormUrlEncoded, form.BodyKind);
        Assert.Equal(["user", "x"], form.FormFields.Select(f => f.Key));
        Assert.Equal([true, false], form.FormFields.Select(f => f.Enabled));

        var upload = Find(import, "Upload");
        Assert.Equal(BodyKind.Multipart, upload.BodyKind);
        Assert.False(upload.MultipartParts[0].IsFile);
        Assert.True(upload.MultipartParts[1].IsFile);
        Assert.Equal(@"C:\temp\a.pdf", upload.MultipartParts[1].Value);

        Assert.Equal(BodyKind.Xml, Find(import, "Typed").BodyKind);
        Assert.Equal(BodyKind.None, Find(import, "Other host").BodyKind);
    }

    [Fact]
    public void Postman_graphql_becomes_a_json_body_with_query_and_variables()
    {
        var request = Find(Import(Postman), "Gql");

        Assert.Equal(BodyKind.Json, request.BodyKind);
        Assert.Contains("\"query\": \"{ me { id } }\"", request.Body);
        Assert.Contains("\"a\": 1", request.Body);
    }

    [Fact]
    public void Postman_path_variables_are_filled_in_from_the_file()
    {
        var import = Import(Postman);

        // ":id" has no value on the request itself, so it stays; the collection variable {{id}} resolved elsewhere.
        Assert.Equal("/files/:id", Find(import, "Upload").Path);
    }

    [Fact]
    public void Postman_other_hosts_keep_their_full_url_and_are_reported()
    {
        var import = Import(Postman);

        Assert.Equal("https://other.test/ping", Find(import, "Other host").Path);
        Assert.Contains(import.Environments, e => e.BaseUrl == "https://other.test");
        Assert.Contains(import.Warnings, w => w.Contains("other hosts") || w.Contains("different host"));
    }

    [Fact]
    public void Postman_losses_are_reported_not_silent()
    {
        var warnings = Import(Postman).Warnings;

        Assert.Contains(warnings, w => w.Contains("scripts"));
        Assert.Contains(warnings, w => w.Contains("oauth2"));
        Assert.Contains(warnings, w => w.Contains("{{token}}"));
        Assert.Contains(warnings, w => w.Contains("unknownHost"));
    }

    [Fact]
    public void A_base_url_with_no_value_becomes_an_empty_base_url_to_fill_in()
    {
        var import = Import("""
        { "info": { "name": "x" }, "item": [ { "name": "a", "request": { "method": "GET", "url": "{{host}}/a" } } ] }
        """);

        Assert.Equal("host", import.Environments[0].Name);
        Assert.Equal(string.Empty, import.Environments[0].BaseUrl);
        Assert.Equal("/a", Find(import, "a").Path);
        Assert.Contains(import.Warnings, w => w.Contains("\"host\""));
    }

    // ---- Insomnia ------------------------------------------------------------------------------

    private const string Insomnia = """
    {
      "_type": "export", "__export_format": 4,
      "resources": [
        { "_id": "wrk_1", "_type": "workspace", "name": "Pets" },
        { "_id": "env_base", "_type": "environment", "parentId": "wrk_1", "name": "Base Environment", "data": { "base_url": "https://pets.test/api", "token": "abc" } },
        { "_id": "env_stage", "_type": "environment", "parentId": "env_base", "name": "Staging", "data": { "base_url": "https://stage.pets.test/api" } },
        { "_id": "fld_1", "_type": "request_group", "parentId": "wrk_1", "name": "Dogs", "metaSortKey": 2 },
        { "_id": "req_b", "_type": "request", "parentId": "fld_1", "name": "Second", "method": "POST", "url": "{{ _.base_url }}/dogs",
          "metaSortKey": 20,
          "body": { "mimeType": "application/json", "text": "{\"n\":1}" },
          "authentication": { "type": "bearer", "token": "{{ _.token }}" },
          "headers": [ { "name": "X-A", "value": "1" }, { "name": "X-B", "value": "2", "disabled": true } ] },
        { "_id": "req_a", "_type": "request", "parentId": "fld_1", "name": "First", "method": "GET", "url": "{{ _.base_url }}/dogs?limit=5",
          "metaSortKey": 10, "parameters": [ { "name": "extra", "value": "y" } ],
          "authentication": { "type": "basic", "username": "u", "password": "p" } },
        { "_id": "req_c", "_type": "request", "parentId": "wrk_1", "name": "Form", "method": "POST", "url": "https://pets.test/api/form", "metaSortKey": 1,
          "body": { "mimeType": "application/x-www-form-urlencoded", "params": [ { "name": "a", "value": "1" } ] },
          "authentication": { "type": "oauth2" } }
      ]
    }
    """;

    [Fact]
    public void Insomnia_workspace_folders_and_sort_order_come_across()
    {
        var import = Import(Insomnia);

        Assert.Equal("Insomnia", import.Format);
        Assert.Equal("Pets", import.Name);
        Assert.Equal(["Form", "Dogs"], import.Items.Select(i => i.Name));
        Assert.Equal(["First", "Second"], import.Items[1].Children.Select(c => c.Name));
    }

    [Fact]
    public void Insomnia_base_url_and_staging_environment_become_the_collections_base_urls()
    {
        var import = Import(Insomnia);

        Assert.Equal(["base_url", "Staging"], import.Environments.Select(e => e.Name));
        Assert.Equal(["https://pets.test/api", "https://stage.pets.test/api"], import.Environments.Select(e => e.BaseUrl));
        Assert.Equal("/dogs", Find(import, "Second").Path);
        Assert.Equal("/form", Find(import, "Form").Path);
    }

    [Fact]
    public void Insomnia_requests_keep_query_headers_body_and_auth()
    {
        var import = Import(Insomnia);

        var first = Find(import, "First");
        Assert.Equal(["limit", "extra"], first.QueryParams.Select(q => q.Key));
        Assert.Equal(AuthKind.Basic, first.Auth);
        Assert.Equal("u", first.BasicUsername);

        var second = Find(import, "Second");
        Assert.Equal(BodyKind.Json, second.BodyKind);
        Assert.Equal("""{"n":1}""", second.Body);
        Assert.Equal("abc", second.BearerToken);
        Assert.Equal([true, false], second.Headers.Select(h => h.Enabled));

        Assert.Equal(BodyKind.FormUrlEncoded, Find(import, "Form").BodyKind);
        Assert.Contains(import.Warnings, w => w.Contains("oauth2"));
    }

    // ---- OpenAPI -------------------------------------------------------------------------------

    private const string OpenApi = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Pet Store", "version": "1" },
      "servers": [ { "url": "https://{env}.pets.test/v2", "description": "Main", "variables": { "env": { "default": "api" } } }, { "url": "/relative" } ],
      "security": [ { "key": [] } ],
      "components": {
        "securitySchemes": {
          "key": { "type": "apiKey", "in": "header", "name": "X-Api-Key" },
          "basic": { "type": "http", "scheme": "basic" },
          "oauth": { "type": "oauth2" }
        },
        "schemas": {
          "Pet": { "type": "object", "required": ["name"], "properties": {
            "id": { "type": "integer" }, "name": { "type": "string", "example": "Rex" },
            "tags": { "type": "array", "items": { "type": "string" } },
            "owner": { "$ref": "#/components/schemas/Owner" } } },
          "Owner": { "type": "object", "properties": { "email": { "type": "string", "format": "email" }, "pet": { "$ref": "#/components/schemas/Pet" } } }
        }
      },
      "paths": {
        "/pets": {
          "get": { "summary": "List pets", "tags": ["pets"],
            "parameters": [
              { "name": "limit", "in": "query", "required": true, "schema": { "type": "integer", "default": 20 } },
              { "name": "cursor", "in": "query", "schema": { "type": "string" } },
              { "name": "X-Trace", "in": "header", "schema": { "type": "string" } },
              { "name": "Accept", "in": "header", "schema": { "type": "string" } } ] },
          "post": { "operationId": "addPet", "tags": ["pets"],
            "security": [ { "basic": [] } ],
            "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } } } } }
        },
        "/pets/{petId}": {
          "parameters": [ { "name": "petId", "in": "path", "required": true, "schema": { "type": "string" } } ],
          "delete": { "tags": ["pets"], "security": [] },
          "put": { "summary": "Form it", "requestBody": { "content": { "application/x-www-form-urlencoded": { "schema": { "type": "object", "properties": { "name": { "type": "string" } } } } } } }
        },
        "/upload": {
          "post": { "summary": "Upload", "security": [ { "oauth": [] } ],
            "requestBody": { "content": { "multipart/form-data": { "schema": { "type": "object", "properties": { "file": { "type": "string", "format": "binary" }, "note": { "type": "string" } } } } } } }
        }
      }
    }
    """;

    [Fact]
    public void OpenApi_operations_are_grouped_by_first_tag_and_named_sensibly()
    {
        var import = Import(OpenApi);

        Assert.Equal("OpenAPI", import.Format);
        Assert.Equal("Pet Store", import.Name);

        var pets = import.Items.Single(i => i.Kind == NodeKind.Folder);
        Assert.Equal("pets", pets.Name);
        Assert.Equal(["List pets", "addPet", "DELETE /pets/{petId}"], pets.Children.Select(c => c.Name));
        Assert.Equal(["Form it", "Upload"], import.Items.Where(i => i.Kind == NodeKind.Request).Select(i => i.Name));
    }

    [Fact]
    public void OpenApi_servers_become_base_urls_with_variable_defaults_filled_in()
    {
        var import = Import(OpenApi);

        Assert.Equal("https://api.pets.test/v2", import.Environments[0].BaseUrl);
        Assert.Equal("Main", import.Environments[0].Name);
        Assert.Equal(string.Empty, import.Environments[1].BaseUrl);
        Assert.Contains(import.Warnings, w => w.Contains("/relative"));
    }

    [Fact]
    public void OpenApi_parameters_become_rows_and_required_ones_are_enabled()
    {
        var request = Find(Import(OpenApi), "List pets");

        Assert.Equal("/pets", request.Path);
        Assert.Equal(["limit", "cursor"], request.QueryParams.Select(q => q.Key));
        Assert.Equal([true, false], request.QueryParams.Select(q => q.Enabled));
        Assert.Equal("20", request.QueryParams[0].Value);
        Assert.Equal(["X-Trace"], request.Headers.Select(h => h.Key));
    }

    [Fact]
    public void OpenApi_json_body_is_built_from_the_schema_following_refs_without_looping()
    {
        var request = Find(Import(OpenApi), "addPet");

        Assert.Equal(BodyKind.Json, request.BodyKind);
        Assert.Contains("\"name\": \"Rex\"", request.Body);
        Assert.Contains("\"email\": \"user@example.com\"", request.Body);
        Assert.Contains("\"tags\": [", request.Body);
    }

    [Fact]
    public void OpenApi_form_and_multipart_bodies_list_the_schema_properties()
    {
        var import = Import(OpenApi);

        var form = Find(import, "Form it");
        Assert.Equal(BodyKind.FormUrlEncoded, form.BodyKind);
        Assert.Equal(["name"], form.FormFields.Select(f => f.Key));

        var upload = Find(import, "Upload");
        Assert.Equal(BodyKind.Multipart, upload.BodyKind);
        Assert.Equal([true, false], upload.MultipartParts.Select(p => p.IsFile));
    }

    [Fact]
    public void OpenApi_security_follows_the_operation_then_the_document()
    {
        var import = Import(OpenApi);

        var inherited = Find(import, "List pets");
        Assert.Equal(AuthKind.ApiKey, inherited.Auth);
        Assert.Equal("X-Api-Key", inherited.ApiKeyName);
        Assert.Equal(ApiKeyLocation.Header, inherited.ApiKeyLocation);

        Assert.Equal(AuthKind.Basic, Find(import, "addPet").Auth);
        Assert.Equal(AuthKind.None, Find(import, "DELETE /pets/{petId}").Auth);
        Assert.Equal(AuthKind.None, Find(import, "Upload").Auth);
        Assert.Contains(import.Warnings, w => w.Contains("oauth"));
    }

    [Fact]
    public void OpenApi_path_parameters_are_left_in_the_path_with_a_warning()
    {
        var import = Import(OpenApi);

        Assert.Equal("/pets/{petId}", Find(import, "DELETE /pets/{petId}").Path);
        Assert.Contains(import.Warnings, w => w.Contains("path parameters"));
    }

    [Fact]
    public void Swagger2_host_base_path_and_body_parameter_are_understood()
    {
        var import = Import("""
        { "swagger": "2.0", "info": { "title": "Old" }, "host": "old.test", "basePath": "/v1/", "schemes": ["https"],
          "securityDefinitions": { "b": { "type": "basic" } }, "security": [ { "b": [] } ],
          "paths": { "/things": { "post": { "summary": "Make",
            "parameters": [ { "name": "body", "in": "body", "schema": { "type": "object", "properties": { "n": { "type": "integer" } } } } ] } } } }
        """);

        Assert.Equal("https://old.test/v1", import.Environments[0].BaseUrl);
        var request = Find(import, "Make");
        Assert.Equal(BodyKind.Json, request.BodyKind);
        Assert.Contains("\"n\": 0", request.Body);
        Assert.Equal(AuthKind.Basic, request.Auth);
    }

    // ---- Into the app --------------------------------------------------------------------------

    [Fact]
    public void An_imported_collection_loads_into_view_models_and_targets_the_first_base_url()
    {
        var import = Import(Postman);

        var collection = CollectionViewModel.FromModel(import.ToModel());

        Assert.Equal("Shop API", collection.Name);
        Assert.Equal("https://api.shop.test/v1", collection.ActiveBaseUrl);

        var node = collection.Items[0].Children[0];
        Assert.Equal("https://api.shop.test/v1/users?page=2", new HttpRequestSender().BuildUrl(collection.ActiveBaseUrl, node.Request!.ToModel()));
    }

    [Fact]
    public void Every_import_gets_ids_of_its_own()
    {
        var first = Import(Postman).ToModel();
        var second = Import(Postman).ToModel();

        Assert.NotEqual(first.Id, second.Id);
    }
}
