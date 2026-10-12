import React, { useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';

type Scenario = { id: string; nom: string; description: string; emprunt: number; operation: string; baseSql: string; disponible: boolean; limitation: string; etapes: string[]; parametres: unknown };
type Agent = { available: boolean; financementsOpen: boolean; executablePath: string; sqlServer: string; requestedDatabase: string; database: string | null; connectionVerified: boolean; connectionStatus: string; busy: boolean };
type Step = { name: string; status: string; message: string; durationMs: number };
type Log = Step & { time: string };
type Run = { id: string; scenarioId: string; scenario: string; status: string; emprunt: number | null; database: string | null; startedAt: string; finishedAt: string | null; steps: Step[]; events: Log[]; errors: string[]; screenshots: string[] };
const labels: Record<string, string> = { lancer_financements: 'Lancer Financements', verifier_connexion: 'Vérifier la connexion SQL', ouvrir_gestion_emprunts: 'Ouvrir la gestion des emprunts', rechercher_emprunt: 'Rechercher l’emprunt', ouvrir_fiche: 'Ouvrir la fiche', verifier_fiche_ouverte: 'Vérifier la bonne fiche', ouvrir_emprunt: 'Ouvrir l’emprunt', revision_taux: 'Réviser le taux', sauvegarder: 'Sauvegarder' };
const stepLabel = (name: string) => labels[name] || name.replaceAll('_', ' ');
const time = (value: string) => new Date(value).toLocaleTimeString('fr-FR');
const badge = (status: string) => <span className={'badge ' + status.toLowerCase()}>{status}</span>;
function initialToken() {
  const token = new URLSearchParams(window.location.hash.slice(1)).get('token');
  if (token) { sessionStorage.setItem('qa-token', token); history.replaceState(null, '', window.location.pathname); }
  return token || sessionStorage.getItem('qa-token') || '';
}
function App() {
  const [token, setToken] = useState(initialToken);
  const [draftToken, setDraftToken] = useState('');
  const [scenarios, setScenarios] = useState<Scenario[]>([]);
  const [agent, setAgent] = useState<Agent | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [focus, setFocus] = useState('ouvrir-emprunt');
  const [borrower, setBorrower] = useState('2261');
  const [runs, setRuns] = useState<Run[]>([]);
  const [run, setRun] = useState<Run | null>(null);
  const [error, setError] = useState('');
  const [requesting, setRequesting] = useState(false);
  const [image, setImage] = useState<string | null>(null);
  const stream = useRef<AbortController | null>(null);
  const scenario = scenarios.find(s => s.id === focus);
  const scenarioRun = run?.scenarioId === scenario?.id ? run : runs.find(r => r.scenarioId === scenario?.id);
  const connectionChecked = scenarioRun?.steps.find(s => s.name === 'verifier_connexion')?.status === 'SUCCESS';
  const connectionVerified = connectionChecked && scenarioRun?.database === scenario?.baseSql;
  const active = run?.status === 'RUNNING' || run?.status === 'QUEUED';
  const busy = active || agent?.busy || requesting;
  async function api<T>(path: string, init?: RequestInit): Promise<T> {
    const response = await fetch('/api' + path, { ...init, headers: { 'Authorization': 'Bearer ' + token, ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...init?.headers } });
    if (!response.ok) {
      const body = await response.text();
      let message = body;
      try { message = JSON.parse(body).message || body; } catch { /* plain HTTP response */ }
      throw new Error(response.status === 401 ? 'Jeton local invalide ou expiré.' : message || 'Erreur HTTP ' + response.status);
    }
    return response.status === 202 && response.headers.get('content-length') === '0' ? undefined as T : response.json();
  }
  async function refresh() {
    try {
      const [status, history] = await Promise.all([api<Agent>('/agent/status'), api<Run[]>('/runs')]);
      setAgent(status); setRuns(history);
    } catch (e) { setError(String(e)); setAgent(null); }
  }
  useEffect(() => {
    if (!token) return;
    let live = true;
    api<Scenario[]>('/scenarios').then(s => { if (live) { setScenarios(s); setSelected(s.filter(x => x.disponible).map(x => x.id)); } }).catch(e => setError(String(e)));
    void refresh();
    const timer = window.setInterval(() => void refresh(), 3000);
    return () => { live = false; clearInterval(timer); stream.current?.abort(); };
  }, [token]);
  useEffect(() => () => { if (image) URL.revokeObjectURL(image); }, [image]);
  async function watch(id: string) {
    stream.current?.abort(); const controller = new AbortController(); stream.current = controller;
    try {
      const response = await fetch(`/api/runs/${id}/events`, { headers: { Authorization: 'Bearer ' + token }, signal: controller.signal });
      if (!response.ok || !response.body) throw new Error('Le flux de progression est indisponible.');
      const reader = response.body.getReader(); const decoder = new TextDecoder(); let pending = '';
      while (true) {
        const { value, done } = await reader.read(); if (done) break;
        pending += decoder.decode(value, { stream: true });
        let end: number;
        while ((end = pending.indexOf('\n\n')) >= 0) {
          const event = pending.slice(0, end); pending = pending.slice(end + 2);
          if (event.startsWith('data: ')) setRun(JSON.parse(event.slice(6)) as Run);
        }
      }
      setRun(await api<Run>(`/runs/${id}`)); await refresh();
    } catch (e) { if (!controller.signal.aborted) { setError(String(e)); await refresh(); } }
  }
  async function start(launchOnly = false) {
    setError(''); setRequesting(true);
    try {
      if (!launchOnly && (!/^\d+$/.test(borrower) || Number(borrower) <= 0 || selected.length !== 1)) throw new Error('Sélectionnez un scénario disponible et un identifiant positif.');
      const started = await api<Run>(launchOnly ? '/agent/launch' : '/runs', { method: 'POST', ...(launchOnly ? {} : { body: JSON.stringify({ scenarioId: selected[0], emprunt: Number(borrower) }) }) });
      setRun(started); void watch(started.id);
    } catch (e) { setError(String(e)); } finally { setRequesting(false); }
  }
  async function cancel() {
    if (!run) return;
    try {
      const response = await fetch(`/api/runs/${run.id}/cancel`, { method: 'POST', headers: { Authorization: 'Bearer ' + token } });
      if (!response.ok) throw new Error('Impossible de demander l’arrêt.');
    } catch (e) { setError(String(e)); }
  }
  async function report(logs = false) {
    if (!run) return;
    try {
      const data = await api<unknown>(`/runs/${run.id}/${logs ? 'logs' : 'result'}`);
      const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }));
      const link = document.createElement('a'); link.href = url; link.download = `${logs ? 'logs' : 'rapport'}-${run.id}.json`; link.click(); window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) { setError(String(e)); }
  }
  async function screenshot() {
    if (!run) return;
    try {
      const response = await fetch(`/api/runs/${run.id}/screenshots/error.png`, { headers: { Authorization: 'Bearer ' + token } });
      if (!response.ok) throw new Error('Aucune capture disponible.');
      setImage(URL.createObjectURL(await response.blob()));
    } catch (e) { setError(String(e)); }
  }
  if (!token) return <div className="login"><div className="brand-icon">Q</div><h1>Quality Automation</h1><p>Connectez ce navigateur à l’agent local Financements.</p><form onSubmit={e => { e.preventDefault(); sessionStorage.setItem('qa-token', draftToken.trim()); setToken(draftToken.trim()); }}><label htmlFor="token">Jeton local</label><input id="token" type="password" autoComplete="off" value={draftToken} onChange={e => setDraftToken(e.target.value)} required/><button className="primary">Se connecter</button></form><p className="muted">Le script de lancement ouvre cette page avec le jeton de votre poste.</p></div>;
  return <div className="app">
    <aside className="rail"><div className="brand-icon">Q</div><span className="rail-active">▦</span><span>◷</span><span className="rail-bottom">QA</span></aside>
    <main>
      <header><div><div className="eyebrow">ATELIER QUALITÉ / APPLICATION LEGACY</div><h1>Quality Automation <span>— Financements</span></h1><p>Scénarios, pilotage Windows et preuves d’exécution.</p></div><div className="local-tag"><i/> POSTE LOCAL</div></header>
      <section className="agent-card"><div className="agent-title"><span className={'dot ' + (agent?.available ? 'green' : '')}/><strong>{agent?.available ? 'Agent Windows connecté' : 'Agent Windows indisponible'}</strong><span className="muted">Session interactive</span><button onClick={() => { setError(''); void refresh(); }}>Détecter Financements</button><button disabled={busy || !agent?.available} onClick={() => void start(true)}>Lancer Financements ↗</button></div><div className="agent-grid"><div><label>APPLICATION</label><strong>{agent?.financementsOpen ? 'Financements ouvert' : 'Financements fermé'}</strong></div><div className="path"><label>EXÉCUTABLE WINDOWS</label><code>{agent?.executablePath || 'Détection en cours…'}</code></div><div><label>BASE SQL CIBLE</label><strong>{agent?.requestedDatabase || 'QA_FIN'} <small className="warning-label">À vérifier au lancement</small></strong></div></div></section>
      {error && <div className="error" role="alert">{error}<button onClick={() => { sessionStorage.removeItem('qa-token'); setToken(''); }}>Reconnecter</button></div>}
      <div className="workspace"><section className="panel scenarios"><div className="panel-heading"><h2>Catalogue de scénarios</h2><span className="count">{scenarios.length}</span></div><div className="selection-tools"><button onClick={() => setSelected(scenarios.filter(s => s.disponible).map(s => s.id))} disabled={busy}>Tout cocher</button><span>·</span><button onClick={() => setSelected([])} disabled={busy}>Tout décocher</button></div><div className="scenario-list">{scenarios.map((s, i) => <div key={s.id} className={'scenario-row ' + (focus === s.id ? 'focused' : '')}><input aria-label={'Sélectionner ' + s.nom} type="checkbox" disabled={!s.disponible || busy} checked={selected.includes(s.id)} onChange={e => setSelected(e.target.checked ? [...selected, s.id] : selected.filter(id => id !== s.id))}/><button className="scenario-name" onClick={() => setFocus(s.id)}><span className="scenario-number">{String(i + 1).padStart(2, '0')}</span><span><strong>{s.nom}</strong><small className={s.disponible ? 'available' : ''}>{s.disponible ? 'Connexion à vérifier' : 'Non disponible'}</small></span></button><span className="chevron">›</span></div>)}</div><div className="catalogue-footer">{selected.length} scénario sélectionné <span>Un seul test à la fois</span></div></section>
      <section className="panel details"><div className="panel-heading"><h2>Détails du scénario</h2><span className="subtle">{scenario?.disponible ? 'LECTURE SEULE' : 'PRÉPARATION'}</span></div>{scenario ? <><div className="detail-body"><div className="eyebrow">SCÉNARIO SÉLECTIONNÉ</div><h3>{scenario.nom}</h3><p>{scenario.description}</p><div className="parameter-grid"><div><label htmlFor="borrower">Identifiant de l’emprunt</label><input id="borrower" type="number" min="1" max="999999999" value={borrower} disabled={busy || !scenario.disponible} onChange={e => setBorrower(e.target.value)}/></div><div><label>Base SQL cible</label><div className="field">{scenario.baseSql}<span>SQL Server</span></div></div><div><label>Opération</label><div className="field">{scenario.operation}</div></div><div><label>Serveur configuré</label><div className="field">{agent?.sqlServer || '—'}</div></div></div><div className="notice"><b>{connectionVerified ? `Connexion ${scenario.baseSql} vérifiée lors de cette exécution` : scenarioRun ? 'Connexion non vérifiée lors de cette exécution' : 'Connexion SQL à vérifier au lancement'}</b><span>{scenarioRun ? `Exécution du ${new Date(scenarioRun.startedAt).toLocaleString('fr-FR')}. ` : ''}{scenario.limitation}</span></div><h4>Étapes prévues <span>{scenario.etapes.length}</span></h4><ol className="steps">{scenario.etapes.map((s, i) => <li key={s}><span>{i + 1}</span>{stepLabel(s)}</li>)}</ol>{scenario.etapes.includes('revision_taux') && <pre className="business-params">{JSON.stringify(scenario.parametres, null, 2)}</pre>}</div><div className="detail-actions"><span>Le résultat exige une vérification réelle.</span><button className="primary" disabled={busy || selected.length !== 1 || !agent?.available || !scenario.disponible || selected[0] !== focus} onClick={() => void start()}>▶ Lancer</button><button className="stop" disabled={!active} onClick={() => void cancel()}>■ Arrêter</button></div></> : <p className="empty">Chargement du catalogue…</p>}</section></div>
      <section className="panel execution"><div className="panel-heading"><h2>Console d’exécution <span className="count">{run?.events.length || 0}</span></h2><div className="toolbar"><button disabled={!run} onClick={() => void report(true)}>Voir les logs</button><button disabled={!run?.screenshots.length} onClick={() => void screenshot()}>Ouvrir les captures</button><button disabled={!run} onClick={() => void report()}>Ouvrir le rapport</button></div></div><div className="run-summary"><span>{run ? run.scenario + (run.emprunt ? ` · Emprunt ${run.emprunt}` : '') : 'Prêt pour une première exécution'}</span>{badge(run?.status || 'NOT_EXECUTED')}<span className="muted">{run ? Math.round((new Date(run.finishedAt || Date.now()).getTime() - new Date(run.startedAt).getTime()) / 1000) + ' s' : 'Aucun scénario exécuté'}</span></div>{run?.steps.length ? <div className="step-strip">{run.steps.map(s => <div key={s.name}><span className={'step-dot ' + s.status.toLowerCase()}/><span>{stepLabel(s.name)}</span>{badge(s.status)}</div>)}</div> : null}<div className="logs"><div className="log-header"><span>HEURE</span><span>ÉTAPE / MESSAGE</span><span>STATUT</span><span>DURÉE</span></div>{run?.events.length ? run.events.map((e, i) => <div className="log-row" key={i}><time>{time(e.time)}</time><div><b>{stepLabel(e.name)}</b><p>{e.message}</p></div>{badge(e.status)}<span>{e.durationMs ? (e.durationMs / 1000).toFixed(2) + ' s' : '—'}</span></div>) : <div className="console-empty"><span>⌁</span><strong>Les étapes s’afficheront ici en direct</strong><p>Sélectionnez un scénario, puis lancez son exécution sur le poste Windows.</p></div>}</div></section>
      <section className="panel history"><div className="panel-heading"><h2>Historique des exécutions</h2><span className="muted">Rapports conservés sur ce poste</span></div>{runs.length ? <div className="history-list">{runs.map(r => <button key={r.id} disabled={active} onClick={() => { stream.current?.abort(); setRun(r); }}><time>{new Date(r.startedAt).toLocaleString('fr-FR')}</time><strong>{r.scenario}</strong><span>{r.emprunt ? '#' + r.emprunt : 'Lancement'}</span>{badge(r.status)}<span>Voir le détail →</span></button>)}</div> : <p className="empty">Aucune exécution enregistrée.</p>}</section>
      <footer>Financements · Automatisation locale <span>Connexion SQL contrôlée avant navigation · Modifications désactivées</span></footer>
    </main>{image && <div className="modal" role="dialog" aria-modal="true" aria-label="Capture de l’exécution"><button onClick={() => setImage(null)}>Fermer ✕</button><img src={image} alt="Capture réelle du bureau lors de l’exécution"/></div>}
  </div>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);
