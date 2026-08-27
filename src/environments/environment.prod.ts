// Production build: the API serves this same build as static files (see Dockerfile),
// so requests are same-origin — no need to hardcode a host.
export const environment = {
  production: true,
  apiOrigin: '',
};
