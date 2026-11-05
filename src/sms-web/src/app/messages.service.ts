import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  MessageList,
  MessageStatus,
  SendMessageRequest,
  SendMessageResponse,
} from './message.models';

/** '/api' is proxied to the API (nginx in Docker, proxy.conf.json in `npm start`), so no CORS is needed. */
const API = '/api/messages';

@Injectable({ providedIn: 'root' })
export class MessagesService {
  private readonly http = inject(HttpClient);

  send(request: SendMessageRequest, idempotencyKey: string) {
    const headers = new HttpHeaders({ 'Idempotency-Key': idempotencyKey });
    return this.http.post<SendMessageResponse>(API, request, { headers });
  }

  list(status: MessageStatus | null) {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return this.http.get<MessageList>(API, { params });
  }
}
