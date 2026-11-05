import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MESSAGE_STATUSES, Message, MessageStatus } from './message.models';
import { MessagesService } from './messages.service';

const REFRESH_MS = 3000;

const STATUS_LABELS: Record<MessageStatus, string> = {
  Pending: 'ממתינה',
  Sending: 'בשליחה',
  Sent: 'נשלחה',
  Failed: 'נכשלה',
};

@Component({
  selector: 'app-messages-table',
  imports: [DatePipe, FormsModule],
  template: `
    <section class="card">
      <div class="table-header">
        <h2>הודעות</h2>
        <label>
          סינון לפי סטטוס:
          <select [ngModel]="status()" (ngModelChange)="setStatus($event)">
            <option [ngValue]="null">הכל</option>
            @for (s of statuses; track s) {
              <option [ngValue]="s">{{ labels[s] }}</option>
            }
          </select>
        </label>
      </div>

      @if (loadError()) {
        <div class="alert error">{{ loadError() }}</div>
      }

      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>זמן יצירה</th>
              <th>נמען</th>
              <th>תוכן</th>
              <th>סטטוס</th>
              <th>ניסיונות</th>
              <th>שגיאה אחרונה</th>
            </tr>
          </thead>
          <tbody>
            @for (m of messages(); track m.id) {
              <tr>
                <td>{{ m.createdAt | date: 'dd/MM/yy HH:mm:ss' }}</td>
                <td>
                  <span dir="ltr">{{ m.to }}</span>
                  @if (m.employees.length) {
                    <div class="muted">{{ employeeNames(m) }}</div>
                  }
                </td>
                <td dir="auto" [title]="m.body">{{ shorten(m.body) }}</td>
                <td>
                  <span [class]="'badge ' + m.status.toLowerCase()">{{ labels[m.status] }}</span>
                </td>
                <td>{{ m.attempts }}</td>
                <td class="error-cell" dir="auto" [title]="m.lastError ?? ''">{{ m.lastError }}</td>
              </tr>
            } @empty {
              <tr>
                <td colspan="6" class="muted center">אין הודעות</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
})
export class MessagesTableComponent {
  private readonly api = inject(MessagesService);

  readonly statuses = MESSAGE_STATUSES;
  readonly labels = STATUS_LABELS;
  readonly status = signal<MessageStatus | null>(null);
  readonly messages = signal<Message[]>([]);
  readonly loadError = signal<string | null>(null);

  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
    this.reload();
  }

  setStatus(status: MessageStatus | null): void {
    this.status.set(status);
    this.reload();
  }

  /** Loads the list; keeps refreshing every 3s while any message is still Pending/Sending. */
  reload(): void {
    clearTimeout(this.timer);
    this.api.list(this.status()).subscribe({
      next: (list) => {
        this.loadError.set(null);
        this.messages.set(list.items);
        if (list.items.some((m) => m.status === 'Pending' || m.status === 'Sending')) {
          this.timer = setTimeout(() => this.reload(), REFRESH_MS);
        }
      },
      error: () => this.loadError.set('טעינת ההודעות נכשלה'),
    });
  }

  employeeNames(m: Message): string {
    return m.employees.map((e) => `${e.firstName} ${e.lastName}`).join(', ');
  }

  shorten(body: string): string {
    return body.length > 40 ? body.slice(0, 40) + '…' : body;
  }
}
