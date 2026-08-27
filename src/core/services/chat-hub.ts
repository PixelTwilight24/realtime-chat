import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { CHAT_HUB_URL } from '../constants/api';
import { Auth } from './auth';

export interface MessageAttachment {
  url: string;
  fileName: string;
  contentType: string;
  size: number;
}

export interface HubMessage {
  id: number;
  senderId: number;
  receiverId: number;
  text: string;
  sentAt: string;
  attachmentUrl: string | null;
  attachmentFileName: string | null;
  attachmentContentType: string | null;
  attachmentSize: number | null;
}

export interface PresenceChange {
  userId: number;
  isOnline: boolean;
}

@Injectable({ providedIn: 'root' })
export class ChatHub {
  private auth = inject(Auth);
  private connection: signalR.HubConnection | null = null;

  readonly messageReceived$ = new Subject<HubMessage>();
  readonly presenceChanged$ = new Subject<PresenceChange>();

  async connect(): Promise<void> {
    if (this.connection) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(CHAT_HUB_URL, { accessTokenFactory: () => this.auth.getToken() ?? '' })
      .withAutomaticReconnect()
      .build();

    connection.on('ReceiveMessage', (message: HubMessage) => this.messageReceived$.next(message));
    connection.on('UserPresenceChanged', (userId: number, isOnline: boolean) =>
      this.presenceChanged$.next({ userId, isOnline })
    );

    await connection.start();
    this.connection = connection;
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    await connection?.stop();
  }

  sendMessage(receiverId: number, text: string, attachment: MessageAttachment | null): Promise<void> {
    if (!this.connection) {
      return Promise.reject(new Error('Not connected to chat server.'));
    }

    return this.connection.invoke('SendMessage', receiverId, text, attachment);
  }
}
