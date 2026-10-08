import { afterEach, describe, expect, it, vi } from "vitest";
import {
  addTrackedGame,
  addGameUser,
  analyzeCsv,
  authenticate,
  getRunningProcesses,
  getTrackedGames,
  removeTrackedGame,
  setActiveGameUser,
} from "./api";

afterEach(() => vi.unstubAllGlobals());

describe("API client", () => {
  it("sends the credentials as JSON to the selected auth endpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ token: "test-token", expiresInSeconds: 3600 }), { status: 200 }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await expect(authenticate("login", "ala", "correct-horse")).resolves.toEqual({
      token: "test-token",
      expiresInSeconds: 3600,
    });

    expect(fetchMock).toHaveBeenCalledWith("/api/auth/login", expect.objectContaining({
      method: "POST",
      body: JSON.stringify({ login: "ala", password: "correct-horse" }),
    }));
  });

  it("attaches the bearer token and CSV file to an analysis request", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ servers: [], players: [] }), { status: 200 }),
    );
    vi.stubGlobal("fetch", fetchMock);
    const file = new File(["csv"], "sessions.csv", { type: "text/csv" });

    await analyzeCsv("secret-token", file);

    const [, request] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(request.headers).toEqual({ Authorization: "Bearer secret-token" });
    expect((request.body as FormData).get("file")).toBe(file);
  });

  it("loads local processes and adds or stops monitoring a game", async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([{ processName: "game" }]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 1 }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 2 }), { status: 201 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(getRunningProcesses("secret-token")).resolves.toEqual([{ processName: "game" }]);
    await expect(getTrackedGames("secret-token")).resolves.toEqual([]);
    await addTrackedGame("secret-token", "Example Game", "game");
    await addGameUser("secret-token", 1, "Ala");
    await setActiveGameUser("secret-token", 1, 2);
    await removeTrackedGame("secret-token", 1);

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      "/api/local-playtime/processes",
      "/api/local-playtime/tracked-games",
      "/api/local-playtime/tracked-games",
      "/api/local-playtime/tracked-games/1/users",
      "/api/local-playtime/tracked-games/1/users/2/active",
      "/api/local-playtime/tracked-games/1",
    ]);
    expect(fetchMock.mock.calls[3][1]).toMatchObject({
      method: "POST",
      body: JSON.stringify({ userName: "Ala" }),
    });
    expect(fetchMock.mock.calls[4][1]).toMatchObject({ method: "PUT" });
  });
});
