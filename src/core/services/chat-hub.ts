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

export interface GroupMemberDto {
  userId: number;
  name: string;
  avatar: string;
  isOnline: boolean;
  isAdmin: boolean;
  joinedAt: string;
}

export interface GroupDto {
  id: number;
  name: string;
  avatar: string | null;
  createdById: number;
  createdAt: string;
  members: GroupMemberDto[];
}

export interface HubGroupMessage {
  id: number;
  groupId: number;
  senderId: number;
  text: string;
  sentAt: string;
  attachmentUrl: string | null;
  attachmentFileName: string | null;
  attachmentContentType: string | null;
  attachmentSize: number | null;
}

export interface GroupMemberRemoved {
  groupId: number;
  userId: number;
}

export interface GroupMemberRoleChanged {
  groupId: number;
  userId: number;
  isAdmin: boolean;
}

export interface GroupRenamedEvent {
  groupId: number;
  name: string;
}

export interface GroupAvatarChangedEvent {
  groupId: number;
  avatar: string;
}

@Injectable({ providedIn: 'root' })
export class ChatHub {
  private auth = inject(Auth);
  private connection: signalR.HubConnection | null = null;

  readonly messageReceived$ = new Subject<HubMessage>();
  readonly presenceChanged$ = new Subject<PresenceChange>();

  readonly groupMessageReceived$ = new Subject<HubGroupMessage>();
  readonly groupCreated$ = new Subject<GroupDto>();
  readonly groupMemberAdded$ = new Subject<GroupDto>();
  readonly groupMemberRemoved$ = new Subject<GroupMemberRemoved>();
  readonly groupMemberRoleChanged$ = new Subject<GroupMemberRoleChanged>();
  readonly groupRenamed$ = new Subject<GroupRenamedEvent>();
  readonly groupAvatarChanged$ = new Subject<GroupAvatarChangedEvent>();
  readonly groupDeleted$ = new Subject<number>();

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

    connection.on('ReceiveGroupMessage', (message: HubGroupMessage) => this.groupMessageReceived$.next(message));
    connection.on('GroupCreated', (group: GroupDto) => this.groupCreated$.next(group));
    connection.on('GroupMemberAdded', (group: GroupDto) => this.groupMemberAdded$.next(group));
    connection.on('GroupMemberRemoved', (groupId: number, userId: number) =>
      this.groupMemberRemoved$.next({ groupId, userId })
    );
    connection.on('GroupMemberRoleChanged', (groupId: number, userId: number, isAdmin: boolean) =>
      this.groupMemberRoleChanged$.next({ groupId, userId, isAdmin })
    );
    connection.on('GroupRenamed', (groupId: number, name: string) => this.groupRenamed$.next({ groupId, name }));
    connection.on('GroupAvatarChanged', (groupId: number, avatar: string) =>
      this.groupAvatarChanged$.next({ groupId, avatar })
    );
    connection.on('GroupDeleted', (groupId: number) => this.groupDeleted$.next(groupId));

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

  sendGroupMessage(groupId: number, text: string, attachment: MessageAttachment | null): Promise<void> {
    if (!this.connection) {
      return Promise.reject(new Error('Not connected to chat server.'));
    }

    return this.connection.invoke('SendGroupMessage', groupId, text, attachment);
  }
}
