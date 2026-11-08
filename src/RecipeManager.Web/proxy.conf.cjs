const target = process.env.RECIPE_MANAGER_API_ORIGIN || 'https://localhost:7243';
const dropCommit = process.env.RECIPE_MANAGER_E2E_DROP_HANDOFF_COMMIT === '1';
if (
  dropCommit &&
  (process.env.RECIPE_MANAGER_E2E_RUN !== '1' || target !== 'https://localhost:7285')
) {
  throw new Error('Commit fault injection is restricted to the owned loopback E2E fixture.');
}
let dropped = false;
const configure = dropCommit
  ? (proxy) => {
      proxy.on('proxyRes', (upstream, request, response) => {
        if (
          !dropped &&
          request.method === 'POST' &&
          /^\/api\/v1\/import-handoffs\/[^/]+\/commit(?:\?.*)?$/.test(request.url) &&
          upstream.statusCode === 201
        ) {
          dropped = true;
          console.info('fixture_commit_response_dropped');
          response.destroy();
        }
      });
    }
  : undefined;
module.exports = {
  '/api/**': { target, secure: true, configure },
  '/hubs/**': { target, secure: true, ws: true },
  '/openapi/**': { target, secure: true },
};
