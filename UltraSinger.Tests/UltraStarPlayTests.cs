using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Blazor.Services;
using UltraSinger.Contracts;
using Xunit;

namespace UltraSinger.Tests;

public class UltraStarPlayTests
{
    private class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; set; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; } =
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        public List<HttpRequestMessage> SentRequests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentRequests.Add(request);
            return await Handler(request);
        }
    }

    [Fact]
    public void ComputeSongHash_MatchesUltraStarPlayAlgorithm()
    {
        var config = new UltraStarPlayConfiguration();
        var options = new TestOptionsMonitor<UltraStarPlayConfiguration>(config);
        var client = new HttpClient();
        var service = new UltraStarPlayService(client, options, NullLogger<UltraStarPlayService>.Instance);

        var artist = "Queen";
        var title = "Bohemian Rhapsody";

        using var md5 = MD5.Create();
        var expectedArtistAndTitleHash = string.Concat(md5.ComputeHash(Encoding.UTF8.GetBytes($"{artist}:{title}")).Select(b => b.ToString("x2")));

        var hashWithoutFile = service.ComputeSongHash(artist, title);
        Assert.Equal(expectedArtistAndTitleHash, hashWithoutFile);

        var dummyFilePath = Path.GetTempFileName();
        try
        {
            var expectedFileHash = string.Concat(md5.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(dummyFilePath))).Select(b => b.ToString("x2")));
            var expectedFullHash = $"{expectedArtistAndTitleHash}:{expectedFileHash}";

            var fullHash = service.ComputeSongHash(artist, title, dummyFilePath);
            Assert.Equal(expectedFullHash, fullHash);
        }
        finally
        {
            File.Delete(dummyFilePath);
        }
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenServerResponds200_ReturnsConnected()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                if (req.RequestUri?.AbsolutePath.Contains("hello") == true)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"Message\":\"Hello UltraSingerUI\"}", Encoding.UTF8, "application/json")
                    });
                }
                if (req.RequestUri?.AbsolutePath.Contains("songs") == true)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"IsSongScanFinished\":true,\"SongCount\":1,\"SongList\":[{\"Artist\":\"Queen\",\"Title\":\"Radio Ga Ga\",\"Hash\":\"12345\"}]}", Encoding.UTF8, "application/json")
                    });
                }
                if (req.RequestUri?.AbsolutePath.Contains("availablePlayers") == true)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"Items\":[\"Player 1\", \"Player 2\"]}", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        };

        var config = new UltraStarPlayConfiguration
        {
            Host = "127.0.0.1",
            HttpPort = 6789,
            Enabled = true
        };
        var options = new TestOptionsMonitor<UltraStarPlayConfiguration>(config);
        var httpClient = new HttpClient(handler);
        var service = new UltraStarPlayService(httpClient, options, NullLogger<UltraStarPlayService>.Instance);

        var status = await service.CheckConnectionAsync();

        Assert.True(status.IsConnected);
        Assert.Equal(1, status.LoadedSongsCount);
        Assert.Equal(2, status.AvailablePlayers.Count);
        Assert.Contains("Player 1", status.AvailablePlayers);
    }

    [Fact]
    public async Task EnqueueSongAsync_SendsCorrectHeadersAndPayload()
    {
        string? capturedBody = null;
        HttpRequestMessage? capturedRequest = null;

        var handler = new MockHttpMessageHandler
        {
            Handler = async req =>
            {
                capturedRequest = req;
                if (req.Content != null)
                {
                    capturedBody = await req.Content.ReadAsStringAsync();
                }

                if (req.RequestUri?.AbsolutePath.Contains("songs") == true)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"IsSongScanFinished\":true,\"SongCount\":0,\"SongList\":[]}", Encoding.UTF8, "application/json")
                    };
                }

                if (req.RequestUri?.AbsolutePath.Contains("availablePlayers") == true)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"Items\":[\"DefaultSinger\"]}", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        };

        var config = new UltraStarPlayConfiguration
        {
            Host = "localhost",
            HttpPort = 6789,
            ClientId = "TestClientId",
            ClientName = "TestClientName",
            Enabled = true
        };
        var options = new TestOptionsMonitor<UltraStarPlayConfiguration>(config);
        var httpClient = new HttpClient(handler);
        var service = new UltraStarPlayService(httpClient, options, NullLogger<UltraStarPlayService>.Instance);

        var result = await service.EnqueueSongAsync("ABBA", "Dancing Queen");

        Assert.True(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("TestClientId", capturedRequest.Headers.GetValues("client-id").FirstOrDefault());
        Assert.Equal("TestClientName", capturedRequest.Headers.GetValues("client-name").FirstOrDefault());

        Assert.NotNull(capturedBody);
        var entry = JsonSerializer.Deserialize<UltraStarPlayQueueEntryDto>(capturedBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(entry);
        Assert.Equal("ABBA", entry.SongDto.Artist);
        Assert.Equal("Dancing Queen", entry.SongDto.Title);
        Assert.Contains("DefaultSinger", entry.SingScenePlayerDataDto.PlayerProfileNames);
    }

    [Fact]
    public async Task GetSongQueueAsync_ParsesQueueEntries()
    {
        var sampleJson = """
        {
            "SongQueueEntries": [
                {
                    "SongDto": {
                        "Artist": "Michael Jackson",
                        "Title": "Billie Jean",
                        "Hash": "abc:123"
                    },
                    "SingScenePlayerDataDto": {
                        "PlayerProfileNames": ["Player 1"],
                        "PlayerProfileToMicProfileMap": {},
                        "PlayerProfileToVoiceIdMap": {}
                    },
                    "GameRoundSettingsDto": null,
                    "IsMedleyWithPreviousEntry": false
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler
        {
            Handler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sampleJson, Encoding.UTF8, "application/json")
            })
        };

        var config = new UltraStarPlayConfiguration { Enabled = true };
        var options = new TestOptionsMonitor<UltraStarPlayConfiguration>(config);
        var httpClient = new HttpClient(handler);
        var service = new UltraStarPlayService(httpClient, options, NullLogger<UltraStarPlayService>.Instance);

        var queue = await service.GetSongQueueAsync();

        Assert.Single(queue);
        Assert.Equal("Michael Jackson", queue[0].SongDto.Artist);
        Assert.Equal("Billie Jean", queue[0].SongDto.Title);
        Assert.Equal("abc:123", queue[0].SongDto.Hash);
        Assert.Equal("Player 1", queue[0].SingScenePlayerDataDto.PlayerProfileNames[0]);
    }
}
