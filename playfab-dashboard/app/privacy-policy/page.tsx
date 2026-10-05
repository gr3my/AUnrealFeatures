import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Privacy Policy Template",
  description: "Customize this privacy-policy template before publishing your game.",
};

export default function PrivacyPolicyPage() {
  return (
    <main className="pp-page">
      <h1>Privacy Policy Template</h1>
      <p className="pp-meta">
        <strong>YOUR GAME NAME</strong> · published by <strong>YOUR STUDIO NAME</strong><br />
        Effective date: YYYY-MM-DD · Last updated: YYYY-MM-DD
      </p>

      <p>
        This page is intentionally a template. Replace every placeholder and review the final
        policy with qualified counsel before publishing it. Your policy should describe the data
        your game actually collects, why it is used, how long it is retained, which processors
        receive it, and how players can exercise applicable privacy rights.
      </p>

      <section>
        <h2>Information you collect</h2>
        <ul>
          <li>Account and platform identifiers: DESCRIBE YOUR DATA.</li>
          <li>Gameplay and progression data: DESCRIBE YOUR DATA.</li>
          <li>Network, device, moderation, analytics, or crash data: DESCRIBE YOUR DATA.</li>
          <li>Voice, commerce, or entitlement data, if applicable: DESCRIBE YOUR DATA.</li>
        </ul>
      </section>

      <section>
        <h2>Third-party services</h2>
        <p>
          List only the services your deployment actually uses and link to their current policies.
          Remove providers that are not part of your game.
        </p>
      </section>

      <section>
        <h2>Retention and deletion</h2>
        <p>
          Explain your retention periods and provide your real deletion-request process.
        </p>
      </section>

      <section>
        <h2>Contact</h2>
        <p>Privacy contact: YOUR PRIVACY EMAIL</p>
        <p>Support contact: YOUR SUPPORT EMAIL</p>
      </section>
    </main>
  );
}
