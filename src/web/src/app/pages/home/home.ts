import { Component, ElementRef, ViewChild, AfterViewChecked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { ChatService } from '../../services/chat';
import { ChatMessage, ChatRequest, DocumentSource } from '../../models/chat';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule
  ],
  templateUrl: './home.html',
  styleUrl: './home.scss'
})
export class HomeComponent implements AfterViewChecked {
  @ViewChild('chatContainer') private chatContainer!: ElementRef;
  
  messages: ChatMessage[] = [];
  currentMessage = '';
  isLoading = false;
  sources: DocumentSource[] = [];
  private shouldScrollToBottom = false;

  constructor(private chatService: ChatService) {}

  ngAfterViewChecked() {
    if (this.shouldScrollToBottom) {
      this.scrollToBottom();
      this.shouldScrollToBottom = false;
    }
  }

  private scrollToBottom(): void {
    try {
      this.chatContainer.nativeElement.scrollTop = this.chatContainer.nativeElement.scrollHeight;
    } catch(err) {
      console.error('Error scrolling to bottom:', err);
    }
  }

  sendMessage() {
    if (!this.currentMessage.trim() || this.isLoading) {
      return;
    }

    // Add user message to chat
    const userMessage: ChatMessage = {
      role: 'user',
      content: this.currentMessage.trim(),
      timestamp: new Date()
    };
    this.messages.push(userMessage);
    this.shouldScrollToBottom = true;

    // Prepare chat request
    const chatRequest: ChatRequest = {
      message: this.currentMessage.trim(),
      history: this.messages.slice(0, -1) // Exclude the current message
    };

    this.currentMessage = '';
    this.isLoading = true;

    // Send to API
    this.chatService.sendMessage(chatRequest).subscribe({
      next: (response) => {
        // Add assistant response to chat
        const assistantMessage: ChatMessage = {
          role: 'assistant',
          content: response.Response,
          timestamp: new Date()
        };
        this.messages.push(assistantMessage);
        this.sources = response.Sources;
        this.isLoading = false;
        this.shouldScrollToBottom = true;
      },
      error: (error) => {
        console.error('Chat error:', error);
        const errorMessage: ChatMessage = {
          role: 'assistant',
          content: 'Sorry, I encountered an error while processing your message. Please try again.',
          timestamp: new Date()
        };
        this.messages.push(errorMessage);
        this.isLoading = false;
        this.shouldScrollToBottom = true;
      }
    });
  }

  onKeyPress(event: KeyboardEvent) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendMessage();
    }
  }

  clearChat() {
    this.messages = [];
    this.sources = [];
  }
}