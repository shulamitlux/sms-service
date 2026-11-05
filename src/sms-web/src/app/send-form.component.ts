import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
} from '@angular/forms';
import { ValidationProblem } from './message.models';
import { MessagesService } from './messages.service';
import { MAX_BODY_LENGTH, countSegments } from './sms-segments';

// Client-side checks are a convenience only; the server does the real validation.
function notBlank(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim().length === 0 ? { required: true } : null;
}

function maxTrimmedLength(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim().length > MAX_BODY_LENGTH ? { maxlength: true } : null;
}

@Component({
  selector: 'app-send-form',
  imports: [ReactiveFormsModule],
  template: `
    <form class="card" [formGroup]="form" (ngSubmit)="submit()">
      <h2>שליחת הודעה</h2>

      <label for="to">מספר טלפון</label>
      <input
        id="to"
        type="tel"
        dir="ltr"
        formControlName="to"
        placeholder="050-1234567"
        autocomplete="off"
      />
      @if (errorFor('to'); as error) {
        <div class="field-error">{{ error }}</div>
      }

      <label for="body">תוכן ההודעה</label>
      <textarea id="body" rows="4" formControlName="body"></textarea>
      <div class="counter" [class.over]="length() > maxLength">
        {{ length() }} / {{ maxLength }} תווים · {{ segments() }} מקטעים
      </div>
      @if (errorFor('body'); as error) {
        <div class="field-error">{{ error }}</div>
      }

      @if (generalError(); as error) {
        <div class="alert error">{{ error }}</div>
      }
      @if (lastSentId(); as id) {
        <div class="alert success">
          ההודעה התקבלה לשליחה (מזהה: <span dir="ltr">{{ id }}</span
          >)
        </div>
      }

      <button type="submit" [disabled]="sending()">{{ sending() ? 'שולח…' : 'שלח' }}</button>
    </form>
  `,
})
export class SendFormComponent {
  private readonly messages = inject(MessagesService);
  private readonly fb = inject(NonNullableFormBuilder);

  readonly sent = output<void>();
  readonly maxLength = MAX_BODY_LENGTH;

  readonly form = this.fb.group({
    to: ['', [notBlank]],
    body: ['', [notBlank, maxTrimmedLength]],
  });

  private readonly body = toSignal(this.form.controls.body.valueChanges, { initialValue: '' });
  readonly length = computed(() => this.body().trim().length);
  readonly segments = computed(() => countSegments(this.body().trim()));

  readonly sending = signal(false);
  readonly generalError = signal<string | null>(null);
  readonly lastSentId = signal<string | null>(null);
  private readonly serverErrors = signal<Record<string, string>>({});

  /** New key for every new form, so a double click / retry of the same form cannot create two messages. */
  private idempotencyKey = crypto.randomUUID();

  errorFor(field: 'to' | 'body'): string | null {
    const server = this.serverErrors()[field];
    if (server) return server;

    const control = this.form.controls[field];
    if (!control.touched || !control.errors) return null;
    if (control.errors['required'])
      return field === 'to' ? 'מספר טלפון הוא שדה חובה' : 'תוכן ההודעה הוא שדה חובה';
    if (control.errors['maxlength']) return `ההודעה ארוכה מדי (מקסימום ${MAX_BODY_LENGTH} תווים)`;
    return null;
  }

  submit(): void {
    this.form.markAllAsTouched();
    this.serverErrors.set({});
    this.generalError.set(null);
    this.lastSentId.set(null);
    if (this.form.invalid || this.sending()) return;

    this.sending.set(true);
    this.messages.send(this.form.getRawValue(), this.idempotencyKey).subscribe({
      next: (response) => {
        this.sending.set(false);
        this.lastSentId.set(response.id);
        this.form.reset();
        this.idempotencyKey = crypto.randomUUID();
        this.sent.emit();
      },
      error: (error: HttpErrorResponse) => {
        this.sending.set(false);
        this.handleError(error);
      },
    });
  }

  private handleError(error: HttpErrorResponse): void {
    const problem = error.error as ValidationProblem | null;
    if (error.status === 400 && problem?.errors) {
      const byField: Record<string, string> = {};
      for (const [key, messages] of Object.entries(problem.errors))
        byField[key.toLowerCase()] = messages[0];
      this.serverErrors.set(byField);
      const other = Object.keys(byField).filter((k) => k !== 'to' && k !== 'body');
      if (other.length) this.generalError.set(other.map((k) => byField[k]).join(' · '));
    } else if (error.status === 409) {
      this.generalError.set(problem?.detail ?? 'ההודעה כבר נשלחה עם תוכן אחר');
    } else if (error.status === 0) {
      this.generalError.set('אין חיבור לשרת. נסו שוב.'); // same key -> a retry will not duplicate
    } else {
      this.generalError.set('אירעה שגיאה בשליחה. נסו שוב.');
    }
  }
}
