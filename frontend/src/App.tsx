import { useState, type FormEvent } from "react";
import {
  addGameUser,
  addTrackedGame,
  analyzeCsv,
  authenticate,
  fetchSecurityBackup,
  getLoginHistory,
  getRunningProcesses,
  getTrackedGames,
  removeTrackedGame,
  setActiveGameUser,
  type AnalysisResult,
  type LoginHistoryEntry,
  type RunningProcess,
  type TrackedGame,
} from "./api";

export default function App() {
  const [mode, setMode] = useState<"login" | "register">("login");
  const [token, setToken] = useState<string | null>(null);
  const [isAdministrator, setIsAdministrator] = useState(false);
  const [login, setLogin] = useState("");
  const [password, setPassword] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<AnalysisResult | null>(null);
  const [processes, setProcesses] = useState<RunningProcess[]>([]);
  const [trackedGames, setTrackedGames] = useState<TrackedGame[]>([]);
  const [loginHistory, setLoginHistory] = useState<LoginHistoryEntry[]>([]);
  const [gameName, setGameName] = useState("");
  const [processName, setProcessName] = useState("");
  const [newUserNames, setNewUserNames] = useState<Record<number, string>>({});
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submitAuth(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    setMessage("");
    setBusy(true);
    try {
      const response = await authenticate(mode, login, password);
      if ("token" in response) {
        setToken(response.token);
        setIsAdministrator(response.isAdministrator ?? false);
        setMessage(response.securityWarning ?? "");
      } else {
        setMode("login");
        setMessage("Konto utworzone. Zaloguj się, aby kontynuować.");
      }
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Wystąpił nieoczekiwany błąd.");
    } finally {
      setBusy(false);
    }
  }

  async function submitFile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !file) return;
    setError("");
    setBusy(true);
    try {
      setResult(await analyzeCsv(token, file));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Wystąpił nieoczekiwany błąd.");
    } finally {
      setBusy(false);
    }
  }

  async function refreshLocalPlaytime() {
    if (!token) return;
    setError("");
    setBusy(true);
    try {
      const [availableProcesses, monitoredGames] = await Promise.all([
        getRunningProcesses(token),
        getTrackedGames(token),
      ]);
      setProcesses(availableProcesses);
      setTrackedGames(monitoredGames);
      if (!processName && availableProcesses.length > 0) {
        setProcessName(availableProcesses[0].processName);
      }
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Nie udało się odczytać lokalnych procesów.");
    } finally {
      setBusy(false);
    }
  }

  async function refreshLoginHistory() {
    if (!token) return;
    setError("");
    setBusy(true);
    try {
      setLoginHistory(await getLoginHistory(token));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Nie udało się odczytać historii logowań.");
    } finally {
      setBusy(false);
    }
  }

  async function downloadBackup() {
    if (!token) return;
    setError("");
    setBusy(true);
    try {
      const backup = await fetchSecurityBackup(token);
      const url = URL.createObjectURL(backup);
      const link = document.createElement("a");
      link.href = url;
      link.download = "server-activity-backup.zip";
      document.body.append(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Nie udało się pobrać backupu.");
    } finally {
      setBusy(false);
    }
  }

  async function startTracking(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !gameName.trim() || !processName) return;
    setError("");
    setBusy(true);
    try {
      await addTrackedGame(token, gameName, processName);
      setGameName("");
      const monitoredGames = await getTrackedGames(token);
      setTrackedGames(monitoredGames);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Nie udało się rozpocząć monitorowania gry.");
    } finally {
      setBusy(false);
    }
  }

  async function stopTracking(id: number) {
    if (!token) return;
    setError("");
    setBusy(true);
    try {
      await removeTrackedGame(token, id);
      setTrackedGames(await getTrackedGames(token));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Nie udało się zatrzymać monitorowania gry.");
    } finally {
      setBusy(false);
    }
  }

  async function resumeTracking(game: TrackedGame) {
    if (!token) return;
    setError("");
    setBusy(true);
    try {
      await addTrackedGame(token, game.gameName, game.processName);
      setTrackedGames(await getTrackedGames(token));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Nie udało się wznowić monitorowania gry.");
    } finally {
      setBusy(false);
    }
  }

  async function submitGameUser(event: FormEvent<HTMLFormElement>, gameId: number) {
    event.preventDefault();
    if (!token) return;
    const userName = newUserNames[gameId]?.trim();
    if (!userName) return;
    setError("");
    setBusy(true);
    try {
      await addGameUser(token, gameId, userName);
      setNewUserNames(current => ({ ...current, [gameId]: "" }));
      setTrackedGames(await getTrackedGames(token));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Nie udało się dodać użytkownika do gry.");
    } finally {
      setBusy(false);
    }
  }

  async function changeActiveGameUser(gameId: number, userId: number) {
    if (!token) return;
    setError("");
    setBusy(true);
    try {
      await setActiveGameUser(token, gameId, userId);
      setTrackedGames(await getTrackedGames(token));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Nie udało się zmienić aktywnego użytkownika.");
    } finally {
      setBusy(false);
    }
  }

  function signOut() {
    setToken(null);
    setIsAdministrator(false);
    setResult(null);
    setFile(null);
    setLoginHistory([]);
    setMessage("");
    setError("");
  }

  return (
    <main className="page">
      <header className="topbar">
        <a className="brand" href="/">PLAYER<span>DATA</span></a>
        {token && <button className="text-button" onClick={signOut}>Wyloguj</button>}
      </header>

      {!token ? (
        <section className="auth-card">
          <p className="eyebrow">ANALITYKA SERWERÓW</p>
          <h1>{mode === "login" ? "Witaj ponownie" : "Utwórz konto"}</h1>
          <p className="muted">Zaloguj się, aby analizować aktywność graczy.</p>
          <form onSubmit={submitAuth}>
            <label htmlFor="login">Login</label>
            <input id="login" autoComplete="username" value={login} onChange={event => setLogin(event.target.value)} required />
            <label htmlFor="password">Hasło</label>
            <input
              id="password"
              type="password"
              autoComplete={mode === "login" ? "current-password" : "new-password"}
              minLength={8}
              value={password}
              onChange={event => setPassword(event.target.value)}
              required
            />
            <button className="primary-button" disabled={busy}>
              {busy ? "Proszę czekać…" : mode === "login" ? "Zaloguj się" : "Zarejestruj"}
            </button>
          </form>
          <button
            className="text-button switch-mode"
            onClick={() => { setMode(mode === "login" ? "register" : "login"); setError(""); setMessage(""); }}
          >
            {mode === "login" ? "Nie masz konta? Zarejestruj się" : "Masz już konto? Zaloguj się"}
          </button>
          {message && <p role="status" className="success-message">{message}</p>}
          {error && <p role="alert" className="error-message">{error}</p>}
        </section>
      ) : (
        <section className="dashboard">
          <p className="eyebrow">PANEL ANALITYCZNY</p>
          <h1>Aktywność serwerów</h1>
          {message && <p role="status" className="success-message">{message}</p>}
          <section className="security-card" aria-labelledby="security-heading">
            <div className="security-heading">
              <div>
                <h2 id="security-heading">Bezpieczeństwo konta</h2>
                <p className="muted">Sprawdź adresy IP użyte podczas rejestracji i logowania.</p>
              </div>
              <div className="security-actions">
                <button className="text-button" onClick={refreshLoginHistory} disabled={busy}>
                  Historia logowań
                </button>
                {isAdministrator && (
                  <button className="text-button" onClick={downloadBackup} disabled={busy}>
                    Pobierz odszyfrowany backup
                  </button>
                )}
              </div>
            </div>
            {loginHistory.length > 0 && (
              <ul className="login-history">
                {loginHistory.map((entry, index) => (
                  <li key={`${entry.createdAt}-${index}`}>
                    <span>
                      <time dateTime={entry.createdAt}>
                        {new Date(entry.createdAt).toLocaleString()}
                      </time>
                      {" — "}
                      {entry.ipAddress ?? "Adres IP niedostępny"}
                      {entry.isNewIp ? " (nowy adres IP)" : ""}
                    </span>
                    {entry.backupStatus === "created" && <span>Backup utworzony</span>}
                    {entry.backupStatus === "failed" && <span className="error-message">Backup nieudany</span>}
                    {entry.backupStatus === "pending" && <span>Backup w toku</span>}
                  </li>
                ))}
              </ul>
            )}
          </section>
          <h2>Lokalny czas gry</h2>
          <p className="muted">
            Monitoruj proces gry na tym komputerze. Pomiar zacznie się od uruchomienia monitorowania.
          </p>
          <button className="text-button" onClick={refreshLocalPlaytime} disabled={busy}>
            {busy ? "Odczytuję…" : "Odśwież procesy i czas"}
          </button>
          <form className="upload-card" onSubmit={startTracking}>
            <label htmlFor="game-name">Nazwa gry</label>
            <input
              id="game-name"
              value={gameName}
              onChange={event => setGameName(event.target.value)}
              maxLength={100}
              required
            />
            <label htmlFor="game-process">Działający proces gry</label>
            <select
              id="game-process"
              value={processName}
              onChange={event => setProcessName(event.target.value)}
              required
            >
              <option value="">Odśwież listę procesów i wybierz grę</option>
              {processes.map(process => (
                <option key={process.processName} value={process.processName}>
                  {process.processName}
                </option>
              ))}
            </select>
            <button className="primary-button" disabled={busy || !gameName.trim() || !processName}>
              Monitoruj grę
            </button>
          </form>
          {trackedGames.length > 0 && (
            <div className="tracked-games">
              {trackedGames.map(game => (
                <article className="tracked-game-card" key={game.id}>
                  <div className="tracked-game-heading">
                    <div>
                      <h3>{game.gameName}</h3>
                      <p className="muted">{game.processName}</p>
                    </div>
                    <div className="tracked-game-status">
                      <span>{(game.totalSeconds / 3600).toFixed(2)} godz. łącznie</span>
                      <span>{!game.isTracking ? "Zatrzymana" : game.isRunning ? "Uruchomiona" : "Oczekiwanie"}</span>
                      {game.isTracking ? (
                        <button className="text-button" onClick={() => stopTracking(game.id)} disabled={busy}>
                          Zatrzymaj
                        </button>
                      ) : (
                        <button className="text-button" onClick={() => resumeTracking(game)} disabled={busy}>
                          Wznów
                        </button>
                      )}
                    </div>
                  </div>
                  <h4>Użytkownicy tej gry</h4>
                  {game.users.length > 0 ? (
                    <ul className="game-user-list">
                      {game.users.map(gameUser => (
                        <li key={gameUser.id}>
                          <span>{gameUser.userName}{gameUser.isActive ? " (aktywny)" : ""}</span>
                          <span>{(gameUser.totalSeconds / 3600).toFixed(2)} godz.</span>
                        </li>
                      ))}
                    </ul>
                  ) : (
                    <p className="muted">Dodaj użytkowników i wybierz, komu naliczać czas gry.</p>
                  )}
                  <form
                    className="game-user-form"
                    onSubmit={event => submitGameUser(event, game.id)}
                  >
                    <label htmlFor={`game-user-${game.id}`}>Dodaj użytkownika</label>
                    <input
                      id={`game-user-${game.id}`}
                      value={newUserNames[game.id] ?? ""}
                      onChange={event => setNewUserNames(current => ({
                        ...current,
                        [game.id]: event.target.value,
                      }))}
                      maxLength={100}
                      required
                    />
                    <button className="primary-button" disabled={busy || !newUserNames[game.id]?.trim()}>
                      Dodaj
                    </button>
                  </form>
                  <label htmlFor={`active-game-user-${game.id}`}>Aktywny użytkownik</label>
                  <select
                    id={`active-game-user-${game.id}`}
                    value={game.users.find(gameUser => gameUser.isActive)?.id ?? ""}
                    onChange={event => {
                      if (event.target.value) {
                        void changeActiveGameUser(game.id, Number(event.target.value));
                      }
                    }}
                    disabled={busy || game.users.length === 0}
                  >
                    <option value="">Wybierz użytkownika do naliczania czasu</option>
                    {game.users.map(gameUser => (
                      <option key={gameUser.id} value={gameUser.id}>{gameUser.userName}</option>
                    ))}
                  </select>
                  <p className="file-hint">
                    Czas uruchomionej gry jest naliczany aktywnemu użytkownikowi.
                  </p>
                </article>
              ))}
            </div>
          )}
          <p className="muted">Prześlij znormalizowany plik CSV z sesjami graczy, aby zobaczyć raport.</p>
          <form className="upload-card" onSubmit={submitFile}>
            <label htmlFor="csv-file">Plik CSV</label>
            <p className="file-hint">Kolumny: service, server_name, game, player, connected_at, disconnected_at</p>
            <input id="csv-file" type="file" accept=".csv,text/csv" onChange={event => setFile(event.target.files?.[0] ?? null)} required />
            <button className="primary-button" disabled={busy || !file}>
              {busy ? "Analizuję…" : "Analizuj dane"}
            </button>
          </form>
          {error && <p role="alert" className="error-message">{error}</p>}
          {result && (
            <>
              <h2>Podsumowanie serwerów</h2>
              <div className="table-wrap">
                <table>
                  <thead><tr><th>Usługa</th><th>Serwer</th><th>Gra</th><th>Sesje</th><th>Gracze</th><th>Godziny</th><th>Śr. min</th><th>Mediana min</th></tr></thead>
                  <tbody>{result.servers.map((row, index) => (
                    <tr key={`${row.service}-${row.serverName}-${row.game}-${index}`}>
                      <td>{row.service}</td><td>{row.serverName}</td><td>{row.game || "—"}</td>
                      <td>{row.sessions}</td><td>{row.uniquePlayers}</td><td>{row.totalHours.toFixed(2)}</td>
                      <td>{row.meanSessionMinutes.toFixed(1)}</td><td>{row.medianSessionMinutes.toFixed(1)}</td>
                    </tr>
                  ))}</tbody>
                </table>
              </div>
              <h2>Podsumowanie graczy</h2>
              <div className="table-wrap">
                <table>
                  <thead><tr><th>Gracz</th><th>Usługa</th><th>Serwer</th><th>Gra</th><th>Sesje</th><th>Godziny</th><th>Śr. min</th><th>Mediana min</th></tr></thead>
                  <tbody>{result.players.map((row, index) => (
                    <tr key={`${row.player}-${row.service}-${row.serverName}-${row.game}-${index}`}>
                      <td>{row.player}</td><td>{row.service}</td><td>{row.serverName}</td><td>{row.game || "—"}</td>
                      <td>{row.sessions}</td><td>{row.totalHours.toFixed(2)}</td>
                      <td>{row.meanSessionMinutes.toFixed(1)}</td><td>{row.medianSessionMinutes.toFixed(1)}</td>
                    </tr>
                  ))}</tbody>
                </table>
              </div>
            </>
          )}
        </section>
      )}
    </main>
  );
}
