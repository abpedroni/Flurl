﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Flurl.Http;
using Flurl.Http.Testing;
using NUnit.Framework;

namespace Flurl.Test.Http
{
	// IFlurlClient and IFlurlRequest both implement IHttpSettingsContainer, which defines a number
	// of settings-related extension methods. This abstract test class allows those methods to be
	// tested against both both client-level and request-level implementations.
	public abstract class SettingsExtensionsTests<T> where T : IHttpSettingsContainer
	{
		protected abstract T GetSettingsContainer();
		protected abstract IFlurlRequest GetRequest(T sc);

		[Test]
		public void can_set_timeout() {
			var sc = GetSettingsContainer().WithTimeout(TimeSpan.FromSeconds(15));
			Assert.AreEqual(TimeSpan.FromSeconds(15), sc.Settings.Timeout);
		}

		[Test]
		public void can_set_timeout_in_seconds() {
			var sc = GetSettingsContainer().WithTimeout(15);
			Assert.AreEqual(sc.Settings.Timeout, TimeSpan.FromSeconds(15));
		}

		[Test]
		public void can_set_header() {
			var sc = GetSettingsContainer().WithHeader("a", 1);
			Assert.AreEqual(("a", "1"), sc.Headers.Single());
		}

		[Test]
		public void can_set_headers_from_anon_object() {
			// null values shouldn't be added
			var sc = GetSettingsContainer().WithHeaders(new { a = "b", one = 2, three = (object)null });
			Assert.AreEqual(2, sc.Headers.Count);
			Assert.IsTrue(sc.Headers.Contains("a", "b"));
			Assert.IsTrue(sc.Headers.Contains("one", "2"));
		}

		[Test]
		public void can_remove_header_by_setting_null() {
			var sc = GetSettingsContainer().WithHeaders(new { a = 1, b = 2 });
			Assert.AreEqual(2, sc.Headers.Count);
			sc.WithHeader("b", null);
			Assert.AreEqual(1, sc.Headers.Count);
			Assert.IsFalse(sc.Headers.Contains("b"));
		}

		[Test]
		public void can_set_headers_from_dictionary() {
			var sc = GetSettingsContainer().WithHeaders(new Dictionary<string, object> { { "a", "b" }, { "one", 2 } });
			Assert.AreEqual(2, sc.Headers.Count);
			Assert.IsTrue(sc.Headers.Contains("a", "b"));
			Assert.IsTrue(sc.Headers.Contains("one", "2"));
		}

		[Test]
		public void underscores_in_properties_convert_to_hyphens_in_header_names() {
			var sc = GetSettingsContainer().WithHeaders(new { User_Agent = "Flurl", Cache_Control = "no-cache" });
			Assert.IsTrue(sc.Headers.Contains("User-Agent"));
			Assert.IsTrue(sc.Headers.Contains("Cache-Control"));

			// make sure we can disable the behavior
			sc.WithHeaders(new { no_i_really_want_underscores = "foo" }, false);
			Assert.IsTrue(sc.Headers.Contains("no_i_really_want_underscores"));

			// dictionaries don't get this behavior since you can use hyphens explicitly
			sc.WithHeaders(new Dictionary<string, string> { { "exclude_dictionaries", "bar" } });
			Assert.IsTrue(sc.Headers.Contains("exclude_dictionaries"));

			// same with strings
			sc.WithHeaders("exclude_strings=123");
			Assert.IsTrue(sc.Headers.Contains("exclude_strings"));
		}

		[Test]
		public void header_names_are_case_insensitive() {
			var sc = GetSettingsContainer().WithHeader("a", 1).WithHeader("A", 2);
			Assert.AreEqual(1, sc.Headers.Count);
			Assert.AreEqual("A", sc.Headers.Single().Name);
			Assert.AreEqual("2", sc.Headers.Single().Value);
		}

		[Test] // #623
		public async Task header_values_are_trimmed() {
			var sc = GetSettingsContainer().WithHeader("a", "   1 \t\r\n");
			sc.Headers.Add("b", "   2   ");

			Assert.AreEqual(2, sc.Headers.Count);
			Assert.AreEqual("1", sc.Headers[0].Value);
			// Not trimmed when added directly to Headers collection (implementation seemed like overkill),
			// but below we'll make sure it happens on HttpRequestMessage when request is sent.
			Assert.AreEqual("   2   ", sc.Headers[1].Value);

			using (var test = new HttpTest()) {
				await GetRequest(sc).GetAsync();
				var sentHeaders = test.CallLog[0].HttpRequestMessage.Headers;
				Assert.AreEqual("1", sentHeaders.GetValues("a").Single());
				Assert.AreEqual("2", sentHeaders.GetValues("b").Single());
			}
		}

		[Test]
		public void can_setup_oauth_bearer_token() {
			var sc = GetSettingsContainer().WithOAuthBearerToken("mytoken");
			Assert.AreEqual(1, sc.Headers.Count);
			Assert.IsTrue(sc.Headers.Contains("Authorization", "Bearer mytoken"));
		}

		[Test]
		public void can_setup_basic_auth() {
			var sc = GetSettingsContainer().WithBasicAuth("user", "pass");
			Assert.AreEqual(1, sc.Headers.Count);
			Assert.IsTrue(sc.Headers.Contains("Authorization", "Basic dXNlcjpwYXNz"));
		}

		[Test]
		public async Task can_allow_specific_http_status() {
			using (var test = new HttpTest()) {
				test.RespondWith("Nothing to see here", 404);
				var sc = GetSettingsContainer().AllowHttpStatus(HttpStatusCode.Conflict, HttpStatusCode.NotFound);
				await GetRequest(sc).DeleteAsync(); // no exception = pass
			}
		}

		[Test]
		public async Task allow_specific_http_status_also_allows_2xx() {
			using (var test = new HttpTest()) {
				test.RespondWith("I'm just an innocent 2xx, I should never fail!", 201);
				var sc = GetSettingsContainer().AllowHttpStatus(HttpStatusCode.Conflict, HttpStatusCode.NotFound);
				await GetRequest(sc).GetAsync(); // no exception = pass
			}
		}

		[Test]
		public void can_clear_non_success_status() {
			using (var test = new HttpTest()) {
				test.RespondWith("I'm a teapot", 418);
				// allow 4xx
				var sc = GetSettingsContainer().AllowHttpStatus("4xx");
				// but then disallow it
				sc.Settings.AllowedHttpStatusRange = null;
				Assert.ThrowsAsync<FlurlHttpException>(async () => await GetRequest(sc).GetAsync());
			}
		}

		[Test]
		public async Task can_allow_any_http_status() {
			using (var test = new HttpTest()) {
				test.RespondWith("epic fail", 500);
				try {
					var sc = GetSettingsContainer().AllowAnyHttpStatus();
					var result = await GetRequest(sc).GetAsync();
					Assert.AreEqual(500, result.StatusCode);
				}
				catch (Exception) {
					Assert.Fail("Exception should not have been thrown.");
				}
			}
		}
	}

	[TestFixture, Parallelizable]
	public class ClientSettingsExtensionsTests : SettingsExtensionsTests<IFlurlClient>
	{
		protected override IFlurlClient GetSettingsContainer() => new FlurlClient();
		protected override IFlurlRequest GetRequest(IFlurlClient client) => client.Request("http://api.com");

		[Test]
		public void WithUrl_shares_client_but_not_Url() {
			var cli = new FlurlClient().WithHeader("myheader", "123");
			var req1 = cli.Request("http://www.api.com/for-req1");
			var req2 = cli.Request("http://www.api.com/for-req2");
			var req3 = cli.Request("http://www.api.com/for-req3");

			CollectionAssert.AreEquivalent(req1.Headers, req2.Headers);
			CollectionAssert.AreEquivalent(req1.Headers, req3.Headers);
			var urls = new[] { req1, req2, req3 }.Select(c => c.Url.ToString());
			CollectionAssert.AllItemsAreUnique(urls);
		}

		[Test]
		public void can_use_uri_with_WithUrl() {
			var uri = new System.Uri("http://www.mysite.com/foo?x=1");
			var req = new FlurlClient().Request(uri);
			Assert.AreEqual(uri.ToString(), req.Url.ToString());
		}

		[Test]
		public void can_override_settings_fluently() {
			using (var test = new HttpTest()) {
				var cli = new FlurlClient().Configure(s => s.AllowedHttpStatusRange = "*");
				test.RespondWith("epic fail", 500);
				var req = "http://www.api.com".ConfigureRequest(c => c.AllowedHttpStatusRange = "2xx");
				req.Client = cli; // client-level settings shouldn't win
				Assert.ThrowsAsync<FlurlHttpException>(async () => await req.GetAsync());
			}
		}
	}

	[TestFixture, Parallelizable]
	public class RequestSettingsExtensionsTests : SettingsExtensionsTests<IFlurlRequest>
	{
		protected override IFlurlRequest GetSettingsContainer() => new FlurlRequest("http://api.com");
		protected override IFlurlRequest GetRequest(IFlurlRequest req) => req;
	}
}