export default function Home() {
  return (
    <main className="flex flex-1 flex-col items-start justify-center gap-4 px-6 py-16 sm:px-8">
      <div className="mx-auto w-full max-w-2xl space-y-3">
        <h1 className="text-3xl font-semibold tracking-tight text-foreground">Jobspect</h1>
        <p className="text-base leading-relaxed text-muted-foreground">
          Track every job application through one pipeline.
        </p>
      </div>
    </main>
  );
}
