import { environment } from '../../environments/environment';

// '' in production means "same origin as the page" — see environment.prod.ts.
export const API_ORIGIN = environment.apiOrigin;

export const API_BASE_URL = `${API_ORIGIN}/api`;

export const CHAT_HUB_URL = `${API_ORIGIN}/hubs/chat`;
