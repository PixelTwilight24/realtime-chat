import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { API_BASE_URL } from '../constants/api';

export interface AuthUser {
  id: number;
  name: string;
  email: string;
  avatar: string;
  gender: string;
  isOnline: boolean;
}

export interface AuthResponse {
  token: string;
  expiresAt: string;
  user: AuthUser;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface SignupRequest {
  name: string;
  email: string;
  password: string;
}

const STORAGE_KEY = 'realtime_chat_auth';

@Injectable({ providedIn: 'root' })
export class Auth {
  private http = inject(HttpClient);

  login(payload: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_BASE_URL}/auth/login`, payload)
      .pipe(tap((response) => this.persistSession(response)));
  }

  signup(payload: SignupRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_BASE_URL}/auth/signup`, payload)
      .pipe(tap((response) => this.persistSession(response)));
  }

  logout(): void {
    localStorage.removeItem(STORAGE_KEY);
  }

  getToken(): string | null {
    return this.getSession()?.token ?? null;
  }

  getUser(): AuthUser | null {
    return this.getSession()?.user ?? null;
  }

  updateStoredUser(user: AuthUser): void {
    const session = this.getSession();
    if (!session) return;

    this.persistSession({ ...session, user });
  }

  isAuthenticated(): boolean {
    const session = this.getSession();
    if (!session) return false;

    return new Date(session.expiresAt).getTime() > Date.now();
  }

  private persistSession(response: AuthResponse): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(response));
  }

  private getSession(): AuthResponse | null {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;

    try {
      return JSON.parse(raw) as AuthResponse;
    } catch {
      return null;
    }
  }
}
