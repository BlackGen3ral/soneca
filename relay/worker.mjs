const hex = /^[a-f0-9]{64}$/;
const windowMs = 30000;
const reply = (value, status = 200) =>
  Response.json(value, { status, headers: { 'Cache-Control': 'no-store' } });

async function digest(value) {
  const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value));
  return Array.from(new Uint8Array(bytes), byte => byte.toString(16).padStart(2, '0')).join('');
}

export default {
  async fetch(request, env) {
    if (request.method !== 'POST') return reply({ error: 'invalid_request' }, 405);
    const path = new URL(request.url).pathname;
    const key = (request.headers.get('Authorization') ?? '').match(/^Bearer ([a-f0-9]{64})$/)?.[1];
    if (!key) return reply({ error: 'unauthorized' }, 401);
    const now = Date.now();

    if (path === '/v1/pc/poll') {
      const phoneHash = request.headers.get('X-Artho-Phone-Hash');
      if (!hex.test(phoneHash ?? '')) return reply({ error: 'invalid_request' }, 400);
      const id = await digest(key);
      const [, command] = await env.DB.batch([
        env.DB.prepare(`INSERT INTO shutdown_mailboxes (id, phone_hash, seen) VALUES (?1, ?2, ?3)
          ON CONFLICT(id) DO UPDATE SET phone_hash = excluded.phone_hash, seen = excluded.seen`).bind(id, phoneHash, now),
        env.DB.prepare(`UPDATE shutdown_mailboxes SET delivered = 1
          WHERE id = ?1 AND issued >= ?2 AND delivered = 0
          RETURNING issued, signature`).bind(id, now - windowMs),
      ]);
      return reply({ command: command.results[0] ?? null });
    }

    if (path === '/v1/pc/receipt' || path === '/v1/phone/receipt') {
      const issuedText = request.headers.get('X-Artho-Issued') ?? '';
      if (!/^\d{13}$/.test(issuedText)) return reply({ error: 'invalid_request' }, 400);
      const issued = Number(issuedText);

      if (path === '/v1/pc/receipt') {
        const id = await digest(key);
        const accepted = await env.DB.prepare(`UPDATE shutdown_mailboxes SET delivered = 2
          WHERE id = ?1 AND issued = ?2 AND delivered IN (1, 2) AND issued >= ?3
          RETURNING issued`).bind(id, issued, now - windowMs).first();
        return accepted ? reply({ received: true }) : reply({ error: 'command_unavailable' }, 404);
      }

      const id = request.headers.get('X-Artho-Mailbox');
      if (!hex.test(id ?? '')) return reply({ error: 'invalid_request' }, 400);
      const command = await env.DB.prepare(`SELECT issued, delivered FROM shutdown_mailboxes
        WHERE id = ?1 AND phone_hash = ?2`).bind(id, await digest(key)).first();
      if (!command) return reply({ error: 'pc_unavailable' }, 404);
      if (command.issued === issued && command.delivered === 2) return reply({ received: true });
      if (command.issued !== issued || issued < now - windowMs) return reply({ expired: true }, 410);
      return reply({ received: false }, 202);
    }

    if (path === '/v1/phone/shutdown') {
      const id = request.headers.get('X-Artho-Mailbox');
      const signature = request.headers.get('X-Artho-Signature');
      const issuedText = request.headers.get('X-Artho-Issued') ?? '';
      const issued = Number(issuedText);
      if (!hex.test(id ?? '') || !hex.test(signature ?? '') ||
          !/^\d{13}$/.test(issuedText) || issued < now - windowMs || issued > now + 5000)
        return reply({ error: 'invalid_request' }, 400);

      const phoneHash = await digest(key);
      const queued = await env.DB.prepare(`UPDATE shutdown_mailboxes
        SET issued = ?3, signature = ?4, delivered = 0
        WHERE id = ?1 AND phone_hash = ?2 AND seen >= ?5
          AND issued < ?3 AND issued < ?6
        RETURNING issued`).bind(id, phoneHash, issued, signature, now - windowMs, now - windowMs).first();
      if (queued) return reply({ queued: true }, 202);
      const previous = await env.DB.prepare(`SELECT seen, issued FROM shutdown_mailboxes
        WHERE id = ?1 AND phone_hash = ?2`).bind(id, phoneHash).first();
      return previous && previous.seen >= now - windowMs
        ? reply({ error: 'already_requested' }, 409)
        : reply({ error: 'pc_unavailable' }, 404);
    }
    return reply({ error: 'not_found' }, 404);
  }
};
