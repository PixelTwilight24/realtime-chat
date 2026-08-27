import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL, API_ORIGIN } from '../../core/constants/api';
import { Auth } from '../../core/services/auth';
import { ChatHub, HubMessage, MessageAttachment } from '../../core/services/chat-hub';

interface User {
  id: number;
  name: string;
  email: string;
  avatar: string;
  isOnline: boolean;
  gender: string;
  lastMessage: string;
}

interface Attachment {
  url: string;
  fileName: string;
  contentType: string;
  size: number;
}

interface ConversationSummary {
  user: Omit<User, 'lastMessage'>;
  lastMessagePreview: string;
  lastMessageAt: string;
}

interface Message {
  id: number;
  text: string;
  isMine: boolean;
  attachment: Attachment | null;
}

const MAX_FILE_SIZE_BYTES = 15 * 1024 * 1024;

@Component({
  selector: 'app-chat',
  imports: [ReactiveFormsModule, FontAwesomeModule],
  templateUrl: './chat.html',
  styleUrl: './chat.css',
})
export class Chat implements OnInit {
  private http = inject(HttpClient);
  private auth = inject(Auth);
  private chatHub = inject(ChatHub);
  private destroyRef = inject(DestroyRef);
  private router = inject(Router);

  private currentUserId = this.auth.getUser()?.id ?? 0;

  showSettings = false;
  showProfileModal = false;

  // The user shown in the profile modal — either the selected contact (read-only)
  // or the signed-in user's own account (editable).
  profileUser = signal<User | null>(null);
  isOwnProfile = signal(false);
  isEditingProfile = signal(false);
  isSavingProfile = signal(false);
  profileSaveError = signal<string | null>(null);

  profileForm = new FormGroup({
    name: new FormControl('', [Validators.required]),
    gender: new FormControl(''),
    avatar: new FormControl('', [Validators.required]),
  });

  // These are all touched from async sources outside template events (HTTP responses,
  // SignalR push events) — this app runs zoneless, so they must be signals or the UI
  // simply won't re-render when the data arrives.
  users = signal<User[]>([]);
  isLoadingUsers = signal(false);
  usersError = signal<string | null>(null);

  selectedUser = signal<User | null>(null);
  messages = signal<Message[]>([]);
  isLoadingMessages = signal(false);

  hubConnected = signal(false);
  hubError = signal<string | null>(null);

  pendingFile = signal<File | null>(null);
  pendingFilePreviewUrl = signal<string | null>(null);
  isUploadingFile = signal(false);
  fileError = signal<string | null>(null);

  // The sidebar only shows conversations you actually have. Search broadens to every
  // user (loaded once, lazily) so starting a conversation with someone new stays possible.
  directoryUsers = signal<User[] | null>(null);

  searchControl = new FormControl('');
  private searchTerm = toSignal(this.searchControl.valueChanges, { initialValue: '' });

  filteredUsers = computed(() => {
    const term = (this.searchTerm() ?? '').trim().toLowerCase();
    if (!term) return this.users();

    const source = this.directoryUsers() ?? this.users();
    return source.filter((user) => user.name.toLowerCase().includes(term));
  });

  chatForm = new FormGroup({
    message: new FormControl(''),
  });

  get canSend(): boolean {
    const hasText = !!this.chatForm.value.message?.trim();
    return this.hubConnected() && !this.isUploadingFile() && (hasText || !!this.pendingFile());
  }

  ngOnInit() {
    this.loadUsers();
    this.connectHub();

    this.searchControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((term) => {
      if (term?.trim() && this.directoryUsers() === null) {
        this.loadDirectoryUsers();
      }
    });

    this.chatHub.messageReceived$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((message) => this.handleIncomingMessage(message));

    this.chatHub.presenceChanged$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ userId, isOnline }) => this.handlePresenceChange(userId, isOnline));

    this.destroyRef.onDestroy(() => {
      this.chatHub.disconnect();
      this.revokePendingFilePreview();
    });
  }

  private async connectHub() {
    try {
      await this.chatHub.connect();
      this.hubConnected.set(true);
    } catch {
      this.hubError.set('Unable to connect to the chat server.');
    }
  }

  private loadUsers() {
    this.isLoadingUsers.set(true);
    this.usersError.set(null);

    this.http.get<ConversationSummary[]>(`${API_BASE_URL}/users/conversations`).subscribe({
      next: (conversations) => {
        // Already ordered by most recent message by the API — preserve that order.
        this.users.set(
          conversations.map((c) => ({ ...c.user, lastMessage: c.lastMessagePreview }))
        );
        this.isLoadingUsers.set(false);
      },
      error: () => {
        this.usersError.set('Unable to load contacts.');
        this.isLoadingUsers.set(false);
      },
    });
  }

  private loadDirectoryUsers() {
    this.http.get<User[]>(`${API_BASE_URL}/users`).subscribe({
      next: (users) => {
        this.directoryUsers.set(
          users
            .filter((user) => user.id !== this.currentUserId)
            .map((user) => ({ ...user, lastMessage: '' }))
        );
      },
    });
  }

  selectUser(user: User) {
    this.selectedUser.set(user);
    this.loadHistory(user.id);
    this.clearPendingFile();
  }

  private loadHistory(otherUserId: number) {
    this.isLoadingMessages.set(true);
    this.messages.set([]);

    this.http
      .get<HubMessage[]>(`${API_BASE_URL}/messages/with/${this.currentUserId}/${otherUserId}`)
      .subscribe({
        next: (history) => {
          if (this.selectedUser()?.id !== otherUserId) return;

          this.messages.set(history.map((message) => this.toLocalMessage(message)));
          this.isLoadingMessages.set(false);
        },
        error: () => {
          if (this.selectedUser()?.id !== otherUserId) return;

          this.isLoadingMessages.set(false);
        },
      });
  }

  private toLocalMessage(message: HubMessage): Message {
    return {
      id: message.id,
      text: message.text,
      isMine: message.senderId === this.currentUserId,
      attachment: message.attachmentUrl
        ? {
            url: `${API_ORIGIN}${message.attachmentUrl}`,
            fileName: message.attachmentFileName ?? 'file',
            contentType: message.attachmentContentType ?? 'application/octet-stream',
            size: message.attachmentSize ?? 0,
          }
        : null,
    };
  }

  private previewTextFor(message: HubMessage): string {
    if (message.text) return message.text;
    if (message.attachmentFileName) return `📎 ${message.attachmentFileName}`;
    return '';
  }

  private handleIncomingMessage(message: HubMessage) {
    const otherPartyId = message.senderId === this.currentUserId ? message.receiverId : message.senderId;
    const preview = this.previewTextFor(message);

    const existing = this.users().find((user) => user.id === otherPartyId);
    if (existing) {
      // A message just happened, so bump this conversation to the top — same as any chat app.
      this.users.update((list) => [
        { ...existing, lastMessage: preview },
        ...list.filter((user) => user.id !== otherPartyId),
      ]);
    } else {
      // First message ever with this person — they weren't in the conversations list yet.
      const known = this.directoryUsers()?.find((user) => user.id === otherPartyId) ?? this.selectedUser();
      const addUser = (user: User) => this.users.update((list) => [{ ...user, lastMessage: preview }, ...list]);

      if (known && known.id === otherPartyId) {
        addUser(known);
      } else {
        this.http.get<User>(`${API_BASE_URL}/users/${otherPartyId}`).subscribe((user) => addUser(user));
      }
    }

    if (this.selectedUser()?.id === otherPartyId) {
      this.messages.update((list) => [...list, this.toLocalMessage(message)]);
    }
  }

  private handlePresenceChange(userId: number, isOnline: boolean) {
    this.users.update((list) => list.map((user) => (user.id === userId ? { ...user, isOnline } : user)));
    this.selectedUser.update((user) => (user && user.id === userId ? { ...user, isOnline } : user));

    // A user found via search (not yet a conversation) lives only in directoryUsers —
    // without this, their presence dot never updates until the whole list reloads.
    this.directoryUsers.update((list) =>
      list ? list.map((user) => (user.id === userId ? { ...user, isOnline } : user)) : list
    );
  }

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.fileError.set(null);

    if (file.size > MAX_FILE_SIZE_BYTES) {
      this.fileError.set('File exceeds the 15 MB limit.');
      return;
    }

    this.revokePendingFilePreview();
    this.pendingFile.set(file);
    this.pendingFilePreviewUrl.set(file.type.startsWith('image/') ? URL.createObjectURL(file) : null);
  }

  clearPendingFile() {
    this.revokePendingFilePreview();
    this.pendingFile.set(null);
    this.fileError.set(null);
  }

  private revokePendingFilePreview() {
    const url = this.pendingFilePreviewUrl();
    if (url) URL.revokeObjectURL(url);
    this.pendingFilePreviewUrl.set(null);
  }

  private uploadPendingFile(file: File): Promise<MessageAttachment> {
    const formData = new FormData();
    formData.append('file', file);

    return firstValueFrom(this.http.post<MessageAttachment>(`${API_BASE_URL}/files/upload`, formData));
  }

  async sendMessage() {
    const text = this.chatForm.value.message?.trim() ?? '';
    const receiver = this.selectedUser();
    const file = this.pendingFile();
    if (!receiver || (!text && !file)) return;

    this.hubError.set(null);

    let attachment: MessageAttachment | null = null;

    if (file) {
      this.isUploadingFile.set(true);
      try {
        attachment = await this.uploadPendingFile(file);
      } catch {
        this.fileError.set('Failed to upload file. Please try again.');
        this.isUploadingFile.set(false);
        return;
      }
      this.isUploadingFile.set(false);
    }

    this.chatForm.reset();
    this.clearPendingFile();

    try {
      await this.chatHub.sendMessage(receiver.id, text, attachment);
    } catch {
      this.hubError.set('Failed to send message. Check your connection and try again.');
    }
  }

  async logout() {
    try {
      await this.chatHub.disconnect();
    } finally {
      this.auth.logout();
      this.router.navigate(['/login']);
    }
  }

  formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  toggleSettings() {
    this.showSettings = !this.showSettings;
  }

  openProfile() {
    this.profileUser.set(this.selectedUser());
    this.isOwnProfile.set(false);
    this.showProfileModal = true;
  }

  openMyProfile() {
    const me = this.auth.getUser();
    if (!me) return;

    this.profileUser.set({ ...me, lastMessage: '' });
    this.isOwnProfile.set(true);
    this.showProfileModal = true;
  }

  closeProfile() {
    this.showProfileModal = false;
    this.isEditingProfile.set(false);
    this.profileSaveError.set(null);
  }

  startEditProfile() {
    const user = this.profileUser();
    if (!user) return;

    this.profileForm.setValue({
      name: user.name,
      gender: user.gender,
      avatar: user.avatar,
    });
    this.profileSaveError.set(null);
    this.isEditingProfile.set(true);
  }

  cancelEditProfile() {
    this.isEditingProfile.set(false);
    this.profileSaveError.set(null);
  }

  saveProfile() {
    if (this.profileForm.invalid) return;

    const { name, gender, avatar } = this.profileForm.value;

    this.isSavingProfile.set(true);
    this.profileSaveError.set(null);

    this.http.put<User>(`${API_BASE_URL}/users/me`, { name, gender, avatar: avatar }).subscribe({
      next: (updated) => {
        const updatedUser: User = { ...updated, lastMessage: '' };

        this.profileUser.set(updatedUser);
        this.auth.updateStoredUser({
          id: updated.id,
          name: updated.name,
          email: updated.email,
          avatar: updated.avatar,
          gender: updated.gender,
          isOnline: updated.isOnline,
        });

        this.isEditingProfile.set(false);
        this.isSavingProfile.set(false);
      },
      error: () => {
        this.profileSaveError.set('Unable to save changes. Please try again.');
        this.isSavingProfile.set(false);
      },
    });
  }
}
