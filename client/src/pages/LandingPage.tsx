import { Link } from 'react-router-dom';

export default function LandingPage() {
  return (
    <div className="min-h-screen flex flex-col">
      <header className="border-b border-border px-8 py-4 flex items-center justify-between">
        <div className="mono uppercase tracking-widest text-accent">FlowBoard</div>
        <nav className="flex gap-2">
          <Link to="/login" className="btn-ghost">
            login
          </Link>
          <Link to="/register" className="btn-primary">
            get started
          </Link>
        </nav>
      </header>

      <main className="flex-1 flex items-center justify-center px-8">
        <div className="max-w-2xl">
          <p className="mono text-text-muted text-xs uppercase tracking-[0.3em] mb-6">
            // a project tool that respects your time
          </p>
          <h1 className="mono text-5xl md:text-6xl leading-tight mb-6">
            Plan. Ship. <span className="text-accent">Measure.</span>
          </h1>
          <p className="text-text-muted text-lg mb-8 max-w-xl">
            A developer-native project management tool. Kanban boards, epics, sprint planning, and a
            velocity chart that doesn't lie. No fluff. No purple gradients.
          </p>
          <div className="flex gap-3">
            <Link to="/register" className="btn-primary px-5 py-2.5">
              create account
            </Link>
            <a
              href="https://github.com/rsandil/flowboard"
              target="_blank"
              rel="noreferrer"
              className="btn px-5 py-2.5"
            >
              view source
            </a>
          </div>
        </div>
      </main>

      <footer className="border-t border-border px-8 py-4 mono text-xs text-text-dim">
        built by Rishabh Sandil · .NET 10 · React · Postgres
      </footer>
    </div>
  );
}
