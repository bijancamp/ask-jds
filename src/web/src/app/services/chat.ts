import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ChatRequest, ChatResponse } from '../models/chat';

@Injectable({
  providedIn: 'root'
})
export class ChatService {
  private apiUrl = 'https://func-api-keanqvp2yvcv4.azurewebsites.net';

  constructor(private http: HttpClient) { }

  sendMessage(request: ChatRequest): Observable<ChatResponse> {
    return this.http.post<ChatResponse>(`${this.apiUrl}/chat`, request);
  }
}